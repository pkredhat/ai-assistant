using System;
namespace AiAssistant.Models
{
    public class QuestionAnswer
    {
        public string? Question { get; set; }
        public string? Answer { get; set; }
        public string? Timestamp { get; set; } // e.g., "00:01:23"
        public float? ConfidenceScore { get; set; } // e.g., 0.97
        public string? Source { get; set; } // e.g., filename or source ID

        public QuestionAnswer(string question, string answer, string timestamp = "", float confidenceScore = 0, string source = "")
        {
            Question = question;
            Answer = answer;
            Timestamp = string.IsNullOrEmpty(timestamp) ? DateTime.Now.ToString("HH:mm:ss") : timestamp;
            ConfidenceScore = confidenceScore;
            Source = source;
        }

        public QuestionAnswer() { }
    }
}