# Quiz goldens

Produced by the **pre-move** WPF code, not by `QuizStore`, so `QuizStoreTests` proves the move changed
nothing. Regenerate only on purpose (a deliberate format or content change), never to make a red test green.

| File | What it pins |
|---|---|
| `quiz_history_golden.json` | `quiz_history.json` as WPF `QuizService.SaveEntry` writes it (Newtonsoft, indented) |
| `custom_quiz_categories_golden.json` | `custom_quiz_categories.json` as WPF `SaveCustomCategory` writes it |
| `quiz_scoring_golden_premove.txt` | trend keys/names, `GetScoreTrend`, built-in categories, archetypes, fallback profiles and questions |

## Recipe

1. Take the pre-move source: `git show 2886855de^:ConditioningControlPanel/Services/Quiz/QuizService.cs`
   (plus the quiz model types it uses from the same commit).
2. Copy the model types and the offline statics verbatim (categories, fallback question/profile, scoring,
   trends, `LoadHistory`/`SaveEntry`, `LoadCustomCategories`/`SaveCustomCategory`) into a throwaway `net8.0`
   console app referencing the same Newtonsoft.Json version. Replace only `App.UserDataPath`/`CorePaths.UserData`
   with a temp directory and drop `App.Logger` calls; change no logic.
3. Run it with `TZ=Europe/Berlin` (the history timestamps are local `DateTime`s; the tests pin the same zone via
   `QuestPersistenceTests.InBerlin`).
4. Two JSON files: build the entries/categories the golden files contain, call the old `SaveEntry` /
   `SaveCustomCategory`, and copy the written files here.
5. The text file: run the body of `QuizStoreTests.ScoringTrendsAndFallbacksMatchPreMoveGolden` with `QuizStore.X`
   read as the old `QuizService.X`, and write `sb.ToString()` here byte-for-byte (LF line endings).
6. Delete the throwaway app. Then `dotnet test Tests/CCP.Core.Tests --filter QuizStore`: the broken-fixture
   tests must still fail on their mutation, and the round-trip tests must pass.
