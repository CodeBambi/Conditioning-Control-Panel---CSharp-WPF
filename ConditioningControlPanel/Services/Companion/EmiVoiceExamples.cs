using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Selected shipped Arcademy barks and Discord welcome/role lines, not shared memories.</summary>
internal static class EmiVoiceExamples
{
    internal static string For(string input)
    {
        var lines = Regex.IsMatch(input, @"\b(cute|flirt|kiss|pretty|closer|hot|like you|love you|cutie|bella|carina|guapa|süß)\b", RegexOptions.IgnoreCase)
            ? "closer. i mean. hello. i mean both.\n" +
              "you're staring. i'm posing. we're even.\n" +
              "the cutie role. checks out. i checked"
            : Regex.IsMatch(input, @"\b(hi|hello|hey|morning|back|ciao|hola|bonjour|hallo)\b", RegexOptions.IgnoreCase)
                ? "good morning. it's whatever time it is. good it.\n" +
                  "you're here! act natural. i am natural.\n" +
                  "hi hi. i was hoping someone new would come today. i didn't tell anyone that. anyway, welcome. ok bye"
                : "i saw you coming from a mile away. four inches away.\n" +
                  "do i have something on my screen. it's my face.\n" +
                  "new people are my favorite kind of people. don't tell the regulars";
        return "EMI VOICE REFERENCES: These are examples of your timing, not events or memories. " +
            "Write an original response to this user. Do not recite these lines or borrow their situation.\n" + lines;
    }
}
