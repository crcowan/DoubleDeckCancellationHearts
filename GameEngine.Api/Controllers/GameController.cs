using GameEngine.Api.Models;
using GameEngine.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameEngine.Api.Controllers
{
    // Scaffolds the single-player API (Human vs 4-10 AI)
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly GameSessionManager _gameManager;
        private readonly AiService _aiService;
        
        // Simulates retrieving human history from 'stats.db'
        private const string DummyHumanHistory = "Player passes high hearts frequently but holds the Queen of Spades until the end.";

        public GameController(GameSessionManager gameManager, AiService aiService)
        {
            _gameManager = gameManager;
            _aiService = aiService;
        }

        [HttpGet("state")]
        public IActionResult GetState()
        {
            return Ok(_gameManager.GetState());
        }

        [HttpPost("reset")]
        public IActionResult ResetGame()
        {
            _gameManager.ResetState();
            return Ok(_gameManager.GetState());
        }

        [HttpPost("reset-hand")]
        public IActionResult ResetHand()
        {
            _gameManager.DealNewHand();
            return Ok(_gameManager.GetState());
        }

        public class StartGameRequest
        {
            public int NumberOfPlayers { get; set; }
            public int AiDifficulty { get; set; }
            public List<string> BotNames { get; set; } = new();
            public GameRules Rules { get; set; } = new();
        }

        [HttpPost("start")]
        public IActionResult StartGame([FromBody] StartGameRequest req)
        {
            var players = new List<Player>();
            
            // Player 1 is always the Human
            players.Add(new Player { Id = "P1", Name = "You", IsAi = false, DifficultyLevel = req.AiDifficulty });
            
            for (int i = 1; i < req.NumberOfPlayers; i++)
            {
                string botName = req.BotNames != null && req.BotNames.Count > i - 1 
                                 ? req.BotNames[i - 1] 
                                 : $"Bot {i}";
                players.Add(new Player { Id = $"AI_{i}", Name = botName, IsAi = true, DifficultyLevel = req.AiDifficulty });
            }

            _gameManager.InitializeGame(players, req.Rules);
            return Ok(_gameManager.GetState());
        }

        public class PlayCardRequest
        {
            public string PlayerId { get; set; } = string.Empty;
            public Suit Suit { get; set; }
            public Rank Rank { get; set; }
        }

        [HttpPost("play")]
        public async Task<IActionResult> PlayCard([FromBody] PlayCardRequest req)
        {
            var cardToPlay = new Card(req.Suit, req.Rank);
            
            var result = _gameManager.PlayCard(req.PlayerId, cardToPlay);
            
            if (!result.Success) return BadRequest(result.ErrorMessage);

            var state = _gameManager.GetState();

            // We removed the synchronous AI loop.
            // The frontend now controls the pace by polling `play-ai` when it sees an AI's turn.
            return Ok(state);
        }

        [HttpPost("play-ai")]
        public async Task<IActionResult> PlayAi()
        {
            var state = _gameManager.GetState();
            
            if (state.Phase != GameState.GamePhase.Playing)
                return BadRequest("Game is not in playing phase.");

            var currentPlayer = state.Players[state.CurrentTurnPlayerIndex];
            
            if (!currentPlayer.IsAi) 
                return BadRequest("Not an AI's turn.");

            var (aiCard, aiReasoning) = await _aiService.GenerateMoveAsync(state, currentPlayer, DummyHumanHistory);
            
            state.LastMoveReasoning[currentPlayer.Id] = aiReasoning;
            _gameManager.PlayCard(currentPlayer.Id, aiCard);

            return Ok(_gameManager.GetState());
        }

        [HttpPost("resolve-trick")]
        public IActionResult ResolveTrick()
        {
            var state = _gameManager.GetState();
            if (state.Phase == GameState.GamePhase.TrickPending)
            {
                _gameManager.CompleteTrick();
            }
            return Ok(state);
        }
        public class PassCardsRequest
        {
            public string PlayerId { get; set; } = string.Empty;
            public List<Card> CardsToPass { get; set; } = new();
        }

        [HttpPost("pass")]
        public IActionResult PassCards([FromBody] PassCardsRequest req)
        {
            var result = _gameManager.PassCards(req.PlayerId, req.CardsToPass);
            if (!result.Success) return BadRequest(result.ErrorMessage);
            
            return Ok(_gameManager.GetState());
        }

        [HttpPost("play-ai-pass")]
        public IActionResult PlayAiPass()
        {
            _aiService.CheckAndPlayAiTurns(_gameManager);
            return Ok(_gameManager.GetState());
        }
    }
}
