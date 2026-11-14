using System.Text.Json.Serialization;

namespace Keypuncher.Core;

/// <summary>What kind of real thing a pick is. The kind of recipe follows from it, not from the model. It lives here,
/// beside <see cref="Recipe"/>, so a saved recipe can carry the kind the director confirmed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RealCategory>))]
public enum RealCategory
{
    /// <summary>A kind of animal alive today.</summary>
    Animal,
    /// <summary>An animal known from fossils or history: dinosaurs, mammoths, the dodo.</summary>
    ExtinctAnimal,
    /// <summary>A plant, flower, tree, fruit or vegetable.</summary>
    Plant,
    Vehicle,
    /// <summary>A real everyday object: a ball, a kite, a teapot.</summary>
    Object,
}
