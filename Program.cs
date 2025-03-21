using AiAssistant.Models;
using DotNetEnv;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AiAssistant
{
    class Program
    {
        const int ChunkDuration = 10;        // Duration (in seconds) for each audio chunk.
        const int totalChunks = 2;         // Total amount of chunks to produce
        static readonly string ModelPath = Environment.GetEnvironmentVariable("MODEL_PATH");        // Path to the Whisper model.
        static OllamaClient _ollamaClient = new OllamaClient();     // Persistent Ollama client instance
        
        static List<QuestionAnswer> Questions = new List<QuestionAnswer>();
        
        static async Task Main(string[] args)
        {
            Env.Load(); // Load .env file
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("⛔ Graceful shutdown requested...");
                cts.Cancel();
            };
            Console.WriteLine("Listening to your conversation..!");
            
            // BlockingCollection to hold recorded chunk file names for processing
            var chunkQueue = new BlockingCollection<string>();

            // Producer Task: Records chunks and adds them to the queue
            Task producer = StartProducerAsync(chunkQueue, cts.Token);

            // Consumer Tasks: Process/transcribe the recorded chunks concurrently
            List<Task> consumerTasks = StartConsumerTasks(chunkQueue);

            // Wait for both the producer and all consumer tasks to complete
            await producer;
            await Task.WhenAll(consumerTasks);

            Console.WriteLine("All chunks have been recorded and transcribed.");

            Console.WriteLine("\n📝 Questions List:");
            foreach (var question in Questions)
            {
                Console.WriteLine("👉 " + question.Question);
                Console.WriteLine("💬 " + question.Answer + "\n\n");
            }
        }

        static List<Task> StartConsumerTasks(BlockingCollection<string> chunkQueue, int consumerCount = 2)
        {
            List<Task> consumerTasks = new List<Task>();
            for (int c = 0; c < consumerCount; c++)
            {
                consumerTasks.Add(Task.Run(async () =>
                {
                    foreach (var chunkFile in chunkQueue.GetConsumingEnumerable())
                    {
                        await ProcessChunkAsync(chunkFile, -1);
                    }
                }));
            }
            return consumerTasks;
        }

        static Task StartProducerAsync(BlockingCollection<string> chunkQueue, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < totalChunks && !cancellationToken.IsCancellationRequested; i++)
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
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("✅ Producer cancelled gracefully.");
                }
                finally
                {
                    // Signal that no more items will be added
                    chunkQueue.CompleteAdding();
                }
            });
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

        static async Task ProcessChunkAsync(string chunkFile, int chunkNumber)
        {
            var whisperCliPath = Environment.GetEnvironmentVariable("WHISPER_CLI") ?? "whisper-cli";
            var whisper = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whisperCliPath,
                    Arguments = $"{chunkFile} --model {ModelPath}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            whisper.Start();
            string transcribed = await whisper.StandardOutput.ReadToEndAsync();
            string source = $"{chunkFile} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            whisper.WaitForExit();
 
            // Extract Questions from Audio
            await ExtractQuestionsAsync(transcribed, source);

            // Clean up the chunk file
            try {
                File.Delete(chunkFile);
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to delete file {chunkFile}: {ex.Message}");
            }
        }

        static async Task ExtractQuestionsAsync(string transcription, string source)
        {
            using var client = new HttpClient();
            var requestBody = new
            {
                text = transcription
            };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            int maxRetries = 3;
            int delayMilliseconds = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var apiUrl = Environment.GetEnvironmentVariable("API_URL");
                    if (string.IsNullOrWhiteSpace(apiUrl)) {
                        throw new InvalidOperationException("API_URL is not defined in the environment or .env file.");
                    }

                    var response = await client.PostAsync(apiUrl, content);
                    response.EnsureSuccessStatusCode();
                    string responseJson = await response.Content.ReadAsStringAsync();

                    var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(responseJson);
                    if (parsed != null && parsed.ContainsKey("questions"))
                    {
                        foreach (var q in parsed["questions"])
                        {
                            string cleanQuestion = RemoveTimestampPrefix(q).Replace("\n", " ").Trim();
                            string answeredQuestion = await SendQuestionToOllama(cleanQuestion);
                            QuestionAnswer qa = new QuestionAnswer(cleanQuestion, answeredQuestion, "", 0f, source);
                            Questions.Add(qa);
                        }
                    }

                    break; // Exit loop if successful
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"❌ API attempt {attempt} failed: {ex.Message}");
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine("⚠️ Max retry attempts reached. Skipping this chunk.");
                        break;
                    }
                    await Task.Delay(delayMilliseconds);
                    delayMilliseconds *= 2; // Exponential backoff
                }
            }
        }

        static async Task<string> SendQuestionToOllama(string question)
        {
            string answer = await _ollamaClient.SendQuestionAsync(question);
            //Console.WriteLine("Received answer: " + answer);
            //Console.WriteLine("----------------------------------");
            return answer;
        }

        // ✅ Helper method to remove timestamps like [00:00:00.000 --> 00:00:01.360]
        static string RemoveTimestampPrefix(string text)
        {
            return Regex.Replace(text, @"^\[\d{2}:\d{2}:\d{2}\.\d{3}( --> \d{2}:\d{2}:\d{2}\.\d{3})?\]\s*", "");
        }
    }
}