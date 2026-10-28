// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Screens.Edit.Verify
{
    /// <summary>
    /// A check of Mapset Verifier, so that its issues can be shown by the same screen as the ones of the built-in checks.
    /// </summary>
    public class MapsetVerifierCheck : ICheck
    {
        // the issue table shows the description of these instead of the category, Mapset Verifier doesn't tell which category a check belongs to.
        public CheckMetadata Metadata { get; }

        public IEnumerable<IssueTemplate> PossibleTemplates => Array.Empty<IssueTemplate>();

        public MapsetVerifierCheck(string description, CheckScope scope)
        {
            Metadata = new CheckMetadata(CheckCategory.Compose, description, scope);
        }

        public IEnumerable<Issue> Run(BeatmapVerifierContext context) => Array.Empty<Issue>();
    }

    public static class MapsetVerifierIssues
    {
        // "01:17:379 - message", with the objects the timestamp refers to in brackets if there are any: "01:17:379 (1,2) - message".
        private static readonly Regex timestamp_prefix = new Regex(@"^(?<time>\d{2,}:[0-5]\d[:.]\d{3})(\s\([^)]+\))?\s-\s", RegexOptions.Compiled);

        /// <summary>
        /// Converts the result of Mapset Verifier to issues: the ones of the set as a whole, and the ones of one difficulty.
        /// </summary>
        /// <param name="json">The result of <see cref="MapsetVerifierServer.RunChecksAsync"/>.</param>
        /// <param name="difficultyName">The difficulty to take the issues of.</param>
        /// <param name="hitObjects">The objects of the difficulty, sorted. The ones at the time of an issue are attached to it, so that clicking it selects them.</param>
        public static List<Issue> Parse(string json, string difficultyName, IReadOnlyList<HitObject> hitObjects)
        {
            var result = JObject.Parse(json);
            var names = ((JObject?)result["checks"])?.Properties().ToDictionary(p => p.Name, p => p.Value.Value<string>("name") ?? string.Empty) ?? new Dictionary<string, string>();
            var checks = new Dictionary<(int, CheckScope), MapsetVerifierCheck>();
            var issues = new List<Issue>();

            void add(JToken? category, CheckScope scope)
            {
                foreach (var item in category?["checkResults"] ?? Enumerable.Empty<JToken>())
                {
                    int id = item.Value<int>("id");

                    if (!checks.TryGetValue((id, scope), out var check))
                        checks[(id, scope)] = check = new MapsetVerifierCheck(names.GetValueOrDefault(id.ToString()) ?? $"Check {id}", scope);

                    string message = item.Value<string>("message") ?? string.Empty;
                    double? time = null;

                    var match = timestamp_prefix.Match(message);

                    if (match.Success && EditorTimestampParser.TryParse(match.Groups["time"].Value, out var parsed, out _))
                    {
                        time = parsed.Value.TotalMilliseconds;
                        message = message[match.Length..];
                    }

                    // the message is used as a format string.
                    var template = new IssueTemplate(check, parseLevel(item.Value<string>("level")), message.Replace("{", "{{").Replace("}", "}}"));

                    issues.Add(time == null
                        ? new Issue(template)
                        : new Issue(hitObjects.Where(h => Math.Abs(h.StartTime - time.Value) < 1).ToList(), template) { Time = time });
                }
            }

            add(result["general"], CheckScope.BeatmapSet);
            add(result["difficulties"]?.FirstOrDefault(d => d.Value<string>("category") == difficultyName), CheckScope.Difficulty);

            return issues;
        }

        // Minor, Info and Check are notes rather than things to fix, so they're hidden like the built-in negligible ones.
        private static IssueType parseLevel(string? level) => level switch
        {
            "Problem" => IssueType.Problem,
            "Warning" => IssueType.Warning,
            "Error" => IssueType.Error,
            _ => IssueType.Negligible,
        };
    }
}
