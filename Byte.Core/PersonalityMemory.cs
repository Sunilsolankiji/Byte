using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Byte.Core;

public enum Interaction { Celebrate, Joke, Nap, Focus, GentleToss, WildToss }
public enum FriendScene { Wave, HighFive, Build, ShowAndTell, Rest }

public sealed class CharacterMemory
{
    public Interaction? LastInteraction { get; set; }
    public int Interactions { get; set; }
    public List<Mood> RecentActivities { get; set; } = new();
    public List<int> RecentJokes { get; set; } = new();
}

public sealed class FriendshipMemory
{
    public int SharedMoments { get; set; }
    public FriendScene? LastScene { get; set; }
}

public sealed class PersonalityMemory
{
    public Dictionary<CharacterKind, CharacterMemory> Characters { get; set; } = new();
    public Dictionary<string, FriendshipMemory> Friendships { get; set; } = new();
    [JsonIgnore] public long Revision { get; private set; }

    public CharacterMemory For(CharacterKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Characters.TryGetValue(kind, out var profile))
        {
            profile = new();
            Characters.Add(kind, profile);
        }
        return profile;
    }

    public FriendshipMemory Between(CharacterKind a, CharacterKind b)
    {
        if (a == b || !Enum.IsDefined(a) || !Enum.IsDefined(b)) throw new ArgumentException("Friendship needs two different characters.");
        var key = (int)a < (int)b ? $"{(int)a}:{(int)b}" : $"{(int)b}:{(int)a}";
        if (!Friendships.TryGetValue(key, out var friendship))
        {
            friendship = new();
            Friendships.Add(key, friendship);
        }
        return friendship;
    }

    public void Remember(CharacterKind kind, Interaction interaction)
    {
        var profile = For(kind);
        profile.LastInteraction = interaction;
        profile.Interactions = Math.Min(10000, profile.Interactions + 1);
        Revision++;
    }

    public void RememberActivity(CharacterKind kind, Mood mood)
    {
        var recent = For(kind).RecentActivities;
        recent.Add(mood);
        if (recent.Count > 3) recent.RemoveAt(0);
        Revision++;
    }

    public int NextJoke(CharacterKind kind, Random random, int count)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
        var recent = For(kind).RecentJokes;
        var available = Enumerable.Range(0, count).Except(recent.TakeLast(count - 1)).ToArray();
        var chosen = available[random.Next(available.Length)];
        recent.Add(chosen);
        while (recent.Count > Math.Min(7, count - 1)) recent.RemoveAt(0);
        Remember(kind, Interaction.Joke);
        return chosen;
    }

    public void CompleteScene(CharacterKind a, CharacterKind b, FriendScene scene)
    {
        var friendship = Between(a, b);
        friendship.SharedMoments = Math.Min(1000, friendship.SharedMoments + 1);
        friendship.LastScene = scene;
        Revision++;
    }

    public FriendScene ChooseScene(CharacterKind a, CharacterKind b, Random random)
    {
        var friendship = Between(a, b);
        if (friendship.SharedMoments == 0) return FriendScene.Wave;
        var options = friendship.SharedMoments < 3
            ? new[] { FriendScene.Wave, FriendScene.ShowAndTell }
            : new[] { FriendScene.Wave, FriendScene.ShowAndTell, FriendScene.HighFive, FriendScene.Build, FriendScene.Rest };
        var available = options.Where(s => s != friendship.LastScene).ToArray();
        return available[random.Next(available.Length)];
    }

    public string Describe(CharacterKind kind)
    {
        var profile = For(kind);
        var trait = kind switch
        {
            CharacterKind.Pip => "Curious, playful. Loves bugs and warm CPUs.",
            CharacterKind.Orbit => "Dreamy explorer. Loves signs and code blocks.",
            _ => "Patient tinkerer. Loves cables and code blocks."
        };
        var last = profile.LastInteraction switch
        {
            Interaction.Celebrate => "Still feeling that little victory.",
            Interaction.Joke => "Saving the next joke for when you ask.",
            Interaction.Nap => "A good nap is part of the algorithm.",
            Interaction.Focus => "Your quiet work buddy.",
            Interaction.GentleToss => "Getting comfortable with gentle landings.",
            Interaction.WildToss => "That was a ride. A quiet moment, please.",
            _ => "Getting to know this tiny desktop."
        };
        var bonds = Enum.GetValues<CharacterKind>().Where(k => k != kind).Select(k =>
        {
            var moments = Between(kind, k).SharedMoments;
            return $"{k}: {(moments >= 8 ? "Good friends" : moments >= 3 ? "Friends" : moments > 0 ? "Getting acquainted" : "Not met yet")}";
        });
        return $"{kind}\n{trait}\n\n{last}\n\n{string.Join("\n", bonds)}";
    }

    public void Clear()
    {
        Characters.Clear();
        Friendships.Clear();
        Revision++;
    }

    public void Validate()
    {
        Characters ??= new();
        Friendships ??= new();
        foreach (var key in Characters.Keys.ToArray())
        {
            if (!Enum.IsDefined(key) || Characters[key] is null) { Characters.Remove(key); continue; }
            var profile = Characters[key];
            profile.Interactions = Math.Clamp(profile.Interactions, 0, 10000);
            if (profile.LastInteraction.HasValue && !Enum.IsDefined(profile.LastInteraction.Value)) profile.LastInteraction = null;
            profile.RecentActivities = (profile.RecentActivities ?? new()).Where(Enum.IsDefined).TakeLast(3).ToList();
            profile.RecentJokes = (profile.RecentJokes ?? new()).Where(j => j >= 0 && j < 8).Distinct().TakeLast(7).ToList();
        }
        foreach (var key in Friendships.Keys.ToArray())
        {
            if (key is not ("0:1" or "0:2" or "1:2") || Friendships[key] is null) { Friendships.Remove(key); continue; }
            var bond = Friendships[key];
            bond.SharedMoments = Math.Clamp(bond.SharedMoments, 0, 1000);
            if (bond.LastScene.HasValue && !Enum.IsDefined(bond.LastScene.Value)) bond.LastScene = null;
        }
    }
}

public static class Personality
{
    public static Mood ChooseActivity(CharacterKind kind, CharacterMemory memory, bool pointerNearby,
        bool canTravel, bool quiet, Random random)
    {
        var choices = new List<(Mood Mood, int Weight)> { (Mood.Sit, 8), (Mood.Sleep, 7) };
        if (!quiet)
        {
            choices.AddRange(new[]
            {
                (Mood.Walk, 18), (Mood.Peek, 4), (Mood.Trip, 2), (Mood.Sign, 4),
                (Mood.Cable, 4), (Mood.Bug, 4), (Mood.Warm, 4), (Mood.Blocks, 4), (Mood.Wave, 3)
            });
            if (pointerNearby) choices.Add((Mood.Inspect, 9));
            if (canTravel) choices.Add((Mood.Windup, 7));
            choices.Add(kind switch
            {
                CharacterKind.Pip => (Mood.Bug, 17),
                CharacterKind.Orbit => (Mood.Sign, 16),
                _ => (Mood.Cable, 17)
            });
            choices.Add(kind switch
            {
                CharacterKind.Pip => (Mood.Warm, 9),
                CharacterKind.Orbit => (Mood.Blocks, 10),
                _ => (Mood.Blocks, 9)
            });
            var learned = memory.LastInteraction switch
            {
                Interaction.Celebrate => Mood.Wave,
                Interaction.Nap or Interaction.WildToss => Mood.Sleep,
                Interaction.Focus => Mood.Sit,
                Interaction.Joke => Mood.Sign,
                _ => Mood.Walk
            };
            choices.Add((learned, 12));
        }
        // Only two poses are available in Quiet mode; never repeat the immediately preceding one.
        var excluded = quiet ? memory.RecentActivities.TakeLast(1) : memory.RecentActivities;
        var candidates = choices.Where(c => !excluded.Contains(c.Mood)).ToArray();
        var pick = random.Next(candidates.Sum(c => c.Weight));
        foreach (var candidate in candidates)
        {
            pick -= candidate.Weight;
            if (pick < 0) return candidate.Mood;
        }
        throw new InvalidOperationException("No personality activity was selected.");
    }
}
