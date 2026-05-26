namespace GameEngine.Api.Models
{
    public class UserConfig
    {
        public bool UseOllama { get; set; } = false;
        public string OllamaEndpoint { get; set; } = "http://localhost:11434";
        public string OllamaModel { get; set; } = "hearts-bot-v1";
    }
}
