using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>Where a living thing is found: in the sea, in fresh water, on land, or in the air. A thing may have
/// several; a frog has fresh water and land.</summary>
[Flags]
public enum Element
{
    None = 0,
    Sea = 1,
    FreshWater = 2,
    Land = 4,
    Air = 8,
    /// <summary>Anything but the open sea: what fits at a lake.</summary>
    NotSea = FreshWater | Land | Air,
    /// <summary>Anything that is not only a freshwater thing: what fits on a beach.</summary>
    NotFreshWater = Sea | Land | Air,
    /// <summary>What fits away from water.</summary>
    Dry = Land | Air,
    All = Sea | FreshWater | Land | Air,
}

/// <summary>
/// The part of "is it found here?" that code can answer and the model could not: a shark does not live in a lake,
/// a trout does not live in a desert. The bench (Nov 2026) found every model tried, large or small, with or
/// without thinking, let a shark, a whale and a jellyfish "live" at Lake Bled, because asked about continents and
/// climates they answer about continents and climates. So the model is asked one more plain fact it knows well,
/// whether the thing lives in the sea, in fresh water, on land or in the air, and the scene's landscape decides
/// what fits. The shapes the toy draws sea creatures with, and a few names, are known without asking.
/// </summary>
public static class Habitat
{
    /// <summary>Things whose element is known for certain, so the model's answer is not needed: the toy's own sea
    /// creatures and a few common picks. Keyed by tidy name.</summary>
    public static readonly IReadOnlyDictionary<string, Element> KnownElements = new Dictionary<string, Element>
    {
        ["whale"] = Element.Sea, ["dolphin"] = Element.Sea, ["shark"] = Element.Sea, ["octopus"] = Element.Sea,
        ["crab"] = Element.Sea, ["seahorse"] = Element.Sea, ["starfish"] = Element.Sea, ["jellyfish"] = Element.Sea,
        ["squid"] = Element.Sea, ["lobster"] = Element.Sea, ["sea turtle"] = Element.Sea, ["seaweed"] = Element.Sea,
        ["kelp"] = Element.Sea, ["coral"] = Element.Sea, ["clownfish"] = Element.Sea, ["tuna"] = Element.Sea,
        ["seal"] = Element.Sea | Element.Land, ["walrus"] = Element.Sea | Element.Land, ["sea lion"] = Element.Sea | Element.Land,
        ["penguin"] = Element.Sea | Element.Land, ["puffin"] = Element.Sea | Element.Land | Element.Air,
        ["polar bear"] = Element.Sea | Element.Land, ["turtle"] = Element.Sea | Element.FreshWater | Element.Land,
        ["fish"] = Element.Sea | Element.FreshWater, ["salmon"] = Element.Sea | Element.FreshWater, ["eel"] = Element.Sea | Element.FreshWater,
        ["trout"] = Element.FreshWater, ["brown trout"] = Element.FreshWater, ["pike"] = Element.FreshWater, ["carp"] = Element.FreshWater,
        ["perch"] = Element.FreshWater, ["grayling"] = Element.FreshWater, ["water lily"] = Element.FreshWater, ["reed"] = Element.FreshWater,
        ["frog"] = Element.FreshWater | Element.Land, ["newt"] = Element.FreshWater | Element.Land, ["otter"] = Element.FreshWater | Element.Land,
        ["beaver"] = Element.FreshWater | Element.Land, ["duck"] = Element.FreshWater | Element.Land | Element.Air,
        ["swan"] = Element.FreshWater | Element.Land | Element.Air, ["mute swan"] = Element.FreshWater | Element.Land | Element.Air,
        ["heron"] = Element.FreshWater | Element.Land | Element.Air, ["kingfisher"] = Element.FreshWater | Element.Air,
    };

    /// <summary>What a thing drawn with <paramref name="shape"/> can be: the sea shapes are sea creatures whatever the
    /// model says, a Fish is a water creature, a Turtle or Seal may come ashore, and the other shapes say nothing.</summary>
    public static Element OfShape(Shape shape) => shape switch
    {
        Shape.Whale or Shape.Dolphin or Shape.Shark or Shape.Octopus or Shape.Crab or Shape.Seahorse or Shape.Starfish or Shape.Jellyfish => Element.Sea,
        Shape.Seal => Element.Sea | Element.Land,
        Shape.Fish => Element.Sea | Element.FreshWater,
        Shape.Turtle => Element.Sea | Element.FreshWater | Element.Land,
        Shape.Duck => Element.FreshWater | Element.Land | Element.Air,
        _ => Element.All,
    };

    /// <summary>The elements a found-there answer names in <c>thingElements</c>, or <see cref="Element.None"/> if it names none.</summary>
    public static Element FromAnswer(JsonElement? answer)
    {
        if (answer is not { ValueKind: JsonValueKind.Object } a || !a.TryGetProperty("thingElements", out var list) || list.ValueKind != JsonValueKind.Array)
            return Element.None;
        var elements = Element.None;
        foreach (var item in list.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && Enum.TryParse<Element>(item.GetString(), out var e) && e is Element.Sea or Element.FreshWater or Element.Land or Element.Air)
                elements |= e;
        return elements;
    }

    /// <summary>Where <paramref name="subject"/> is found: a known name decides; otherwise the shape it is drawn with
    /// narrows what the model said, and if they disagree outright the shape wins, since the shape is what the child
    /// sees. With no shape and no answer, anywhere.</summary>
    public static Element Of(string subject, Shape? shape, JsonElement? answer)
    {
        var tidy = RealThingRules.Tidy(subject);
        if (KnownElements.TryGetValue(tidy, out var known)) return known;
        // "harbour seal", "river otter": the last word often names the kind.
        var last = tidy.Split(' ')[^1];
        if (last != tidy && KnownElements.TryGetValue(last, out known)) return known;
        var byShape = shape is { } s ? OfShape(s) : Element.All;
        var said = FromAnswer(answer);
        if (said == Element.None) return byShape;
        var both = byShape & said;
        return both != Element.None ? both : byShape == Element.All ? said : byShape;
    }

    /// <summary>What can be found in <paramref name="landscape"/>: no sea creatures at a lake, no freshwater fish on a
    /// beach, nothing that lives only in water anywhere dry. No landscape allows everything.</summary>
    public static Element Allowed(Landscape? landscape) => landscape switch
    {
        null => Element.All,
        Landscape.Lake => Element.NotSea,
        Landscape.Beach => Element.NotFreshWater,
        _ => Element.Dry,
    };

    /// <summary>Whether a thing of <paramref name="elements"/> can be found in <paramref name="landscape"/>.</summary>
    public static bool Fits(Element elements, Landscape? landscape) => (elements & Allowed(landscape)) != Element.None;
}
