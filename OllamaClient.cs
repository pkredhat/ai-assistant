using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AiAssistant
{
    public class OllamaClient : IDisposable
    {
        // Sends a question by launching a new process each time.
        public async Task<string> SendQuestionAsync(string question)
        {
            //Console.WriteLine($"Sending question: {question}");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ollama",
                    // Pass the question as part of the arguments.
                    // Adjust quoting if necessary.
                    Arguments = $"run granite3.2 \"{question}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Read the entire output.
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine("[Error] " + error);
            }
            return output.Trim();
        }

        public void Dispose()
        {
            // Nothing to dispose since we're not keeping a persistent process.
        }
    }
}