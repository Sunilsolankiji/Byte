using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Byte.Core;

internal static class PersonalityTests
{
    public static void Run(Action<bool, string> check, Display main)
    {
        var memory = new PersonalityMemory();
        var settings = new Settings { FriendsTogether = true, Activity = ActivityLevel.Playful };
        var random = new Random(901);
        foreach (var kind in Enum.GetValues<CharacterKind>())
        {
            var jokes = Enumerable.Range(0, 8).Select(_ => memory.NextJoke(kind, random, 8)).ToArray();
            check(jokes.Distinct().Count() == 8, kind + " exhausts the joke deck before repeating");
            var saved = JsonSerializer.Deserialize<PersonalityMemory>(JsonSerializer.Serialize(memory))!;
            saved.Validate();
            var next = saved.NextJoke(kind, random, 8);
            check(!jokes.TakeLast(7).Contains(next), kind + " avoids recent jokes across a restart");
            for (var i = 0; i < 500; i++)
            {
                var recent = memory.For(kind).RecentActivities.ToArray();
                var choice = Personality.ChooseActivity(kind, memory.For(kind), true, true, false, random);
                if (recent.Contains(choice)) throw new InvalidOperationException("Autonomous personality repeated a recent activity.");
                memory.RememberActivity(kind, choice);
            }
            check(memory.For(kind).RecentActivities.Count == 3, kind + " keeps autonomous memory bounded and excludes the last three activities");
            var prev = (Mood?)null;
            for (var i = 0; i < 30; i++)
            {
                var choice = Personality.ChooseActivity(kind, memory.For(kind), true, true, true, random);
                if (choice == prev || choice is not (Mood.Sit or Mood.Sleep)) throw new Exception("Quiet mode personality moved or repeated.");
                prev = choice;
                memory.RememberActivity(kind, choice);
            }
            check(true, kind + " remains quiet with nonrepeating still poses under reduced motion");
        }
        foreach (var kind in Enum.GetValues<CharacterKind>())
        {
            var choices = Enumerable.Range(0, 2000)
                .Select(_ => Personality.ChooseActivity(kind, new(), false, false, false, random)).ToArray();
            var favorite = kind switch { CharacterKind.Byte => Mood.Cable, CharacterKind.Pip => Mood.Bug, _ => Mood.Sign };
            check(choices.Count(c => c == favorite) > 300 && !choices.Contains(Mood.Inspect) && !choices.Contains(Mood.Windup),
                kind + " has a measurable distinct preference and respects pointer/travel gates");
        }
        var napChoices = Enumerable.Range(0, 1500).Select(_ => Personality.ChooseActivity(CharacterKind.Byte,
            new CharacterMemory { LastInteraction = Interaction.Nap }, false, false, false, random)).Count(c => c == Mood.Sleep);
        var normalChoices = Enumerable.Range(0, 1500).Select(_ => Personality.ChooseActivity(CharacterKind.Byte,
            new CharacterMemory(), false, false, false, random)).Count(c => c == Mood.Sleep);
        check(napChoices > normalChoices * 1.6, "recent user-requested naps measurably influence later idle decisions");

        memory = new();
        var byteBot = new Companion(new[] { main }, settings, 55, memory);
        byteBot.UserAct(Mood.Celebrate);
        check(memory.For(CharacterKind.Byte).LastInteraction == Interaction.Celebrate, "explicit celebrations are remembered");
        byteBot.Kind = CharacterKind.Pip;
        byteBot.UserAct(Mood.Sleep);
        check(memory.For(CharacterKind.Pip).LastInteraction == Interaction.Nap &&
            memory.For(CharacterKind.Byte).LastInteraction == Interaction.Celebrate, "switching character identities preserves separate memories");
        byteBot.Act(Mood.Idle);
        byteBot.StartDrag();
        byteBot.Drag(new PointD(600, main.Work.Bottom - 80));
        byteBot.Release(new PointD(20, 0));
        var sawGentleReaction = false;
        for (var i = 0; i < 70; i++)
        {
            byteBot.Tick(.05, default, false);
            sawGentleReaction |= byteBot.Mood == Mood.Stretch;
        }
        check(memory.For(CharacterKind.Pip).LastInteraction == Interaction.GentleToss && sawGentleReaction,
            "Pip remembers gentle handling and stretches after landing");
        byteBot.StartDrag();
        byteBot.Drag(new PointD(600, main.Work.Bottom - 80));
        byteBot.Release(new PointD(1200, 0));
        var sawWildReaction = false;
        for (var i = 0; i < 70; i++)
        {
            byteBot.Tick(.05, default, false);
            sawWildReaction |= byteBot.Mood == Mood.Sit;
        }
        check(memory.For(CharacterKind.Pip).LastInteraction == Interaction.WildToss && sawWildReaction,
            "energetic tossing gets a quiet recovery rather than more excitement");
        var beforeCancel = memory.For(CharacterKind.Pip).Interactions;
        byteBot.StartDrag(); byteBot.CancelDrag();
        check(memory.For(CharacterKind.Pip).Interactions == beforeCancel, "lost capture and lock recovery do not fabricate interactions");

        memory.Clear();
        var buddies = Enum.GetValues<CharacterKind>().Take(2)
            .Select((k, i) => new Companion(new[] { main }, settings, 30 + i, memory) { Kind = k }).ToArray();
        var social = new SocialBehavior(33);
        void Ready()
        {
            social.Cancel();
            foreach (var c in buddies) { c.Act(Mood.Idle); c.Paused = false; c.ReducedMotion = false; if (c.IsFocusing) c.StopFocus(); c.Act(Mood.Idle); }
            SocialBehavior.Gather(buddies, main.Work.Center, new[] { main });
        }
        void Start()
        {
            for (var i = 0; i < 1000 && !social.Active; i++) social.Tick(.05, buddies, settings, false);
            if (!social.Active) throw new Exception("Expected social scene to start.");
        }
        void Finish()
        {
            for (var i = 0; i < 300 && social.Active; i++)
            {
                social.Tick(.05, buddies, settings, false);
                foreach (var c in buddies) c.Tick(.05, default, false);
            }
            if (social.Active) throw new Exception("Social scene did not finish within its bound.");
        }
        Ready(); Start();
        social.Cancel();
        check(memory.Between(CharacterKind.Byte, CharacterKind.Pip).SharedMoments == 0,
            "canceled and interrupted encounters do not grow friendship");
        Ready(); Start(); Finish();
        check(memory.Between(CharacterKind.Byte, CharacterKind.Pip).SharedMoments == 1 &&
            memory.Between(CharacterKind.Pip, CharacterKind.Byte).LastScene == FriendScene.Wave,
            "only a completed scene grows a symmetric friendship");
        var scenes = new List<FriendScene>();
        for (var i = 0; i < 8; i++)
        {
            Ready(); Start(); Finish();
            scenes.Add(memory.Between(CharacterKind.Byte, CharacterKind.Pip).LastScene!.Value);
        }
        check(memory.Describe(CharacterKind.Byte).Contains("Pip: Good friends") &&
            scenes.Zip(scenes.Skip(1)).All(pair => pair.First != pair.Second),
            "friendship progresses visibly and consecutive shared scenes do not repeat");
        var familiar = Enumerable.Range(0, 100)
            .Select(_ => memory.ChooseScene(CharacterKind.Byte, CharacterKind.Pip, random)).ToArray();
        check(familiar.Any(s => s is FriendScene.HighFive or FriendScene.Build or FriendScene.Rest),
            "familiar friends unlock high-fives, collaborative builds and quiet shared rests");
        Ready();
        var speeches = 0;
        foreach (var c in buddies) c.Speech += _ => speeches++;
        buddies[0].UserAct(Mood.Celebrate);
        social.Tick(.05, buddies, settings, false);
        check(buddies[1].IsReacting && buddies[1].Mood == Mood.Stretch,
            "a nearby Pip reacts to a friend's celebration in character");
        check(buddies[1].SignalVersion == 0 && speeches == 0, "friend reactions never cascade or start unsolicited speech");
        buddies[1].Tick(.05, default, true);
        check(!buddies[1].IsReacting && buddies[1].Mood == Mood.Sit, "busy input cancels a friend reaction immediately");
        Ready();
        buddies[0].UserAct(Mood.Sleep);
        social.Tick(.05, buddies, settings, false);
        check(!buddies[1].IsReacting, "reaction cooldown prevents immediate repeated responses");
        var freshSocial = new SocialBehavior(42);
        Ready();
        buddies[1].Summon(new PointD(main.Work.Right, main.Work.Bottom));
        buddies[0].Summon(new PointD(main.Work.Left, main.Work.Bottom));
        buddies[0].UserAct(Mood.Celebrate);
        freshSocial.Tick(.05, buddies, settings, false);
        check(!buddies[1].IsReacting, "far-away friends do not react through the desktop");
        Ready();
        buddies[1].StartFocus(1);
        buddies[0].UserAct(Mood.Celebrate);
        freshSocial.Tick(.05, buddies, settings, false);
        check(!buddies[1].IsReacting && !freshSocial.Active, "focus suppresses contextual reactions as well as shared scenes");
        buddies[1].StopFocus();
        buddies[1].Act(Mood.Idle);
        freshSocial.Tick(.05, buddies, settings, false);
        check(!buddies[1].IsReacting, "events seen during focus are not replayed afterward");
        Ready();
        buddies[0].UserAct(Mood.Celebrate);
        freshSocial.Tick(.05, buddies, settings, true);
        freshSocial.Tick(.05, buddies, settings, false);
        check(!buddies[1].IsReacting, "events during busy or hidden time are consumed without replay");
        Ready();
        buddies[0].UserAct(Mood.Celebrate);
        buddies[0].CancelDrag();
        new SocialBehavior(72).Tick(.05, buddies, settings, false);
        check(!buddies[1].IsReacting && buddies[0].Signal is null, "lock/capture cancellation clears unobserved signals instead of replaying them after unlock");
        Ready();
        buddies[0].UserAct(Mood.Celebrate);
        var toggledSocial = new SocialBehavior(89);
        toggledSocial.Tick(.05, buddies, settings, false);
        check(buddies[1].IsReacting, "nearby reactions can start again after an explicit new event");
        settings.AutoInteractions = false;
        toggledSocial.Tick(.05, buddies, settings, false);
        check(!buddies[1].IsReacting, "disabling automatic interactions cancels a reaction already in progress");
        settings.AutoInteractions = true;
        buddies[0].UserAct(Mood.Celebrate);
        buddies[0].Kind = CharacterKind.Orbit;
        check(buddies[0].Signal is null && buddies[0].Memory.For(CharacterKind.Byte).LastInteraction == Interaction.Celebrate,
            "switching a skin clears transient signals without moving stored identity memories");
        buddies[0].Kind = CharacterKind.Byte;

        var encoded = JsonSerializer.Serialize(memory);
        var restored = JsonSerializer.Deserialize<PersonalityMemory>(encoded)!;
        restored.Validate();
        check(restored.Between(CharacterKind.Pip, CharacterKind.Byte).SharedMoments == 9 &&
            !encoded.Contains("Revision") && !encoded.Contains("Pointer"), "bounded identity and friendship memory survives serialization without activity tracking");
        var invalid = JsonSerializer.Deserialize<PersonalityMemory>(
            "{\"Characters\":{\"99\":{},\"0\":{\"Interactions\":-5,\"LastInteraction\":99,\"RecentActivities\":[0,999,1,2,3,4],\"RecentJokes\":[-1,0,1,2,3,4,5,6,7,99]}},\"Friendships\":{\"0:2\":{\"SharedMoments\":99999,\"LastScene\":99},\"unknown\":{}}}")!;
        invalid.Validate();
        check(invalid.Characters.Count == 1 && invalid.For(CharacterKind.Byte).Interactions == 0 &&
            invalid.For(CharacterKind.Byte).LastInteraction is null && invalid.For(CharacterKind.Byte).RecentActivities.Count == 3 &&
            invalid.For(CharacterKind.Byte).RecentJokes.Count == 7 && invalid.Friendships.Count == 1 &&
            invalid.Between(CharacterKind.Byte, CharacterKind.Orbit).SharedMoments == 1000,
            "invalid and oversized local memory is normalized to fixed identity and history bounds");
        var nulls = JsonSerializer.Deserialize<PersonalityMemory>("{\"Characters\":null,\"Friendships\":null}")!;
        nulls.Validate();
        check(nulls.Characters.Count == 0 && nulls.Friendships.Count == 0, "null memory collections recover safely");
        restored.Clear();
        check(restored.Characters.Count == 0 && restored.Friendships.Count == 0 && restored.Revision > 0,
            "forgetting clears every stored personality and bond");
    }
}
