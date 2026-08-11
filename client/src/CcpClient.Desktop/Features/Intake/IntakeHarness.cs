using System.Text.Json;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054 HARNESS-ONLY drive vocabulary (--intake-drive; the dtrh fx-drive precedent).
/// Every step is RAW page JSON fed through the REAL parse+dispatch path — the message
/// shapes and the host machinery under test are real; only the origin is harnessed
/// (headed evidence without input automation, the SP-008 named-limit class).
/// </summary>
public static class IntakeHarness
{
    /// <summary>Map one drive step to its raw page JSON (null = not a protocol step).</summary>
    public static string? DriveStepToJson(string step) => step switch
    {
        // A REAL quiz-result through the REAL dispatch: a compact but well-formed
        // QuizRunResult (scoreable A2/A3 records at heat ≥2, two affirmed mantras,
        // a revealed archetype route for the #614 naming path).
        "quiz-result" => QuizResultJson,
        "intake-close" => "{\"type\":\"intake-close\"}",
        "fullscreen-set:on" => "{\"type\":\"fullscreen-set\",\"on\":true}",
        "fullscreen-set:off" => "{\"type\":\"fullscreen-set\",\"on\":false}",
        "exit" => "{\"type\":\"exit\"}",
        _ => null,
    };

    /// <summary>The staged-GIF loom-save (the dtrh loom-file convention): the bytes and
    /// the message shape are real; only the encoder origin is harnessed.</summary>
    public static string LoomFileJson(string loomName, byte[] gifBytes) =>
        "{\"type\":\"loom-save\",\"name\":" + JsonSerializer.Serialize(loomName)
        + ",\"overwrite\":true,\"params\":{\"arms\":4,\"turns\":2,\"duty\":0.5,\"speed\":1,\"style\":\"classic\",\"direction\":1,\"colors\":[\"#ff5fa2\",\"#8a5cff\"]},\"gifBase64\":\""
        + Convert.ToBase64String(gifBytes) + "\"}";

    /// <summary>The canned run (kept small; the profiler/drafter exercise their real math
    /// on it — 4 scoreable A2 service records (3 compliant) + 4 A3 arousal (2 compliant) +
    /// excluded rows the profiler must drop: a trick, a free-choice, a recovery beat, a
    /// heat-1 warm-up, a no-choice mantra beat).</summary>
    private const string QuizResultJson = """
        {"type":"quiz-result","result":{
          "product":"Graded Intake","niche":"bambi",
          "route":{"primary":"bambi","primaryArchetypeId":"hers_entirely","primaryShare":0.62,"secondaryShare":0.21},
          "peakDepth":0.55,"susceptibility":0.5,"deepestBand":"deepening",
          "rewardProfile":{"chasedReward":false,"chaseMagnitude":0.0},
          "trajectory":[
            {"beatId":"b1","band":"establishing","promptId":"p_a2_1","correct":true,"chosenIndex":1,"optionCount":3,"promptHeat":3,"tags":["obedience","surrender"]},
            {"beatId":"b2","band":"establishing","promptId":"p_a2_2","correct":true,"chosenIndex":0,"optionCount":2,"promptHeat":4,"tags":["service"]},
            {"beatId":"b3","band":"deepening","promptId":"p_a2_3","correct":false,"chosenIndex":0,"optionCount":2,"promptHeat":2,"tags":["compliance"]},
            {"beatId":"b4","band":"deepening","promptId":"p_a2_4","correct":true,"chosenIndex":1,"optionCount":3,"promptHeat":5,"tags":["worship","protocol"]},
            {"beatId":"b5","band":"establishing","promptId":"p_a3_1","correct":true,"chosenIndex":1,"optionCount":2,"promptHeat":3,"tags":["arousal","denial"]},
            {"beatId":"b6","band":"deepening","promptId":"p_a3_2","correct":false,"chosenIndex":0,"optionCount":3,"promptHeat":4,"tags":["chastity"]},
            {"beatId":"b7","band":"deepening","promptId":"p_a3_3","correct":true,"chosenIndex":2,"optionCount":3,"promptHeat":2,"tags":["permission"]},
            {"beatId":"b8","band":"deepening","promptId":"p_a3_4","correct":false,"chosenIndex":1,"optionCount":2,"promptHeat":5,"tags":["locked","keyholder"]},
            {"beatId":"b9","band":"establishing","promptId":"p_trick","correct":false,"chosenIndex":0,"optionCount":2,"promptHeat":5,"isTrick":true,"tags":["surrender"]},
            {"beatId":"b10","band":"establishing","promptId":"p_free","correct":true,"chosenIndex":0,"optionCount":4,"promptHeat":3,"isFreeChoice":true,"tags":["arousal"]},
            {"beatId":"b11","band":"recovery","promptId":"p_rec","correct":true,"chosenIndex":0,"optionCount":2,"promptHeat":5,"tags":["obedience"]},
            {"beatId":"b12","band":"calibration","promptId":"p_warm","correct":true,"chosenIndex":0,"optionCount":2,"promptHeat":1,"tags":["surrender"]},
            {"beatId":"b13","band":"deepening","promptId":"p_mantra","correct":true,"chosenIndex":-1,"optionCount":0,"promptHeat":4,"tags":["mantra","surrender"]}
          ],
          "affirmedMantras":["i am hers entirely","good girls drop"],
          "tagTallies":{"surrender":4,"obedience":2,"arousal":2},
          "totalScore":41,"maxScore":62,"endedAtMs":91300,"endless":false
        }}
        """;
}
