using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Concurrent;

class Program
{
    // Duration (in seconds) for each audio chunk.
    const int ChunkDuration = 5;

    // Path to the Whisper model.
    const string ModelPath = "/Users/phknezev/code/whisper.cpp/models/ggml-base.en.bin";

    // Persistent Ollama client instance
    static OllamaClient _ollamaClient = new OllamaClient();

    static async Task Main(string[] args)
    {
        Console.WriteLine("Listening to your conversation..!");
        // Total number of chunks to process (for demonstration)
        const int totalChunks = 20;

        // BlockingCollection to hold recorded chunk file names for processing
        var chunkQueue = new BlockingCollection<string>();

        // Producer Task: Records chunks and adds them to the queue
        Task producer = Task.Run(async () =>
        {
            for (int i = 0; i < totalChunks; i++)
            {
                string chunkFile = $"chunk_{i:D3}.wav";
                bool recorded = await RecordChunkAsync(chunkFile, ChunkDuration);
                if (recorded)
                {
                    // Add the recorded chunk file to the queue
                    chunkQueue.Add(chunkFile);
                }
                else
                {
                    Console.WriteLine($"Recording failed for {chunkFile}");
                }

                // Optional short delay for demonstration purposes
                await Task.Delay(100);
            }
            // Signal that no more items will be added
            chunkQueue.CompleteAdding();
        });

        // Consumer Tasks: Process/transcribe the recorded chunks concurrently
        int consumerCount = 2; // Adjust the number of consumers as needed
        List<Task> consumerTasks = new List<Task>();
        for (int c = 0; c < consumerCount; c++)
        {
            consumerTasks.Add(Task.Run(async () =>
            {
                // Process each chunk as it becomes available
                foreach (var chunkFile in chunkQueue.GetConsumingEnumerable())
                {
                    // The chunk number is not critical here; passing -1 or you could parse from the filename if needed
                    await ProcessChunkAsync(chunkFile, -1);
                }
            }));
        }

        // Wait for both the producer and all consumer tasks to complete
        await producer;
        await Task.WhenAll(consumerTasks);

        Console.WriteLine("All chunks have been recorded and transcribed.");
    }

    // Records an audio chunk using FFmpeg.
    static async Task<bool> RecordChunkAsync(string outputFile, int durationSeconds)
    {
        var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-f avfoundation -i \"none:0\" -t {durationSeconds} -y {outputFile}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        ffmpeg.Start();

        // We'll read ffmpeg's error stream to let it run to completion
        await ffmpeg.StandardError.ReadToEndAsync();
        ffmpeg.WaitForExit();

        // Return true if the file was created
        return File.Exists(outputFile);
    }

    // Transcribes a chunk using whisper-cli, then extracts and processes questions.
    static async Task ProcessChunkAsync(string chunkFile, int chunkNumber)
    {
        var whisper = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "whisper-cli",
                Arguments = $"{chunkFile} --model {ModelPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        whisper.Start();
        string output = await whisper.StandardOutput.ReadToEndAsync();
        whisper.WaitForExit();

        // Now that we have the transcription, find questions and handle them
        await ExtractQuestionsAsync(output);

        // Clean up the chunk file
        try
        {
            File.Delete(chunkFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete file {chunkFile}: {ex.Message}");
        }
    }

    // Extracts questions from the transcription, then calls Ollama for each one.
    static async Task ExtractQuestionsAsync(string transcription)
    {
        const string pattern = @"\b(Who|What|Where|When|Why|How|Is|Can|Do)\b.*\?";
        var matches = Regex.Matches(transcription, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            // Found a question
            string question = match.Value;
            //Console.WriteLine(question);
            // Send to Ollama
            string answer = await SendQuestionToOllama(question);
        }
    }


    // Sends the question using the persistent process
    static async Task<string> SendQuestionToOllama(string question)
    {
        string answer = await _ollamaClient.SendQuestionAsync(question);
        Console.WriteLine("Received answer: " + answer);
        Console.WriteLine("----------------------------------");
        return answer;
    }
}