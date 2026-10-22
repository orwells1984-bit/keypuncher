// Prompt playground: ask the director for one recipe and print it.
//
//   dotnet run --project tools/Keypuncher.Sandbox -- gemma3:4b "a round sleepy cat"

using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Inference.Ollama;

var model = args.Length > 0 ? args[0] : "gemma3:4b";
var gloss = args.Length > 1 ? args[1] : "a smiling sun";

var lm = new OllamaLanguageModel(new Uri("http://localhost:11434"), model);
var world = new WorldSummary("meadow", "morning", "playful", 0, 0.2, []);

var result = await lm.GenerateJsonAsync(DirectorPrompts.System, DirectorPrompts.NewRecipe(world, gloss), RecipeSchema.Element);
Console.WriteLine(result.RawText);
Console.WriteLine();

if (RecipeValidator.TryParse(result.Json, out var recipe, out var error))
    Console.WriteLine("valid: " + JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true }));
else
    Console.WriteLine("rejected: " + error);

Console.WriteLine($"{result.Timing.OutputTokens} tokens at {result.Timing.OutputTokensPerSecond:0.0} tok/s, total {result.Timing.Total.TotalSeconds:0.0}s");
