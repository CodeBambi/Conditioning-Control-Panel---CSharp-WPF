namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the 4-digit subject id (`intake_subject.txt`; IntakeHostService.cs:969-988
/// parity). Local fiction — minted once, persisted, handed to the page on init (the
/// intro/outro greeting "Subject #NNNN"), NEVER transmitted anywhere (the privacy
/// boundary verbatim). Atomic .tmp-swap write; an unreadable/corrupt file re-mints (the
/// id is a greeting, not an identity — re-mint is honest, logged).
/// </summary>
public static class IntakeSubjectId
{
    /// <summary>Load the persisted id or mint + persist a new one (0001-9999, zero-padded;
    /// WPF mints 1..9998 — :970-988). Presence+shape logging only.</summary>
    public static string LoadOrMint(string filePath, Action<string> log, Random? rng = null)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath).Trim();
                if (text.Length == 4 && int.TryParse(text, out var existing) && existing is >= 1 and <= 9999)
                {
                    return text;
                }

                log("intake: subject id file unreadable — re-minting (the id is a greeting, not an identity)");
            }
        }
        catch (Exception ex)
        {
            log($"intake: subject id read failed ({ex.GetType().Name}) — re-minting");
        }

        var id = (1 + (rng ?? Random.Shared).Next(9998)).ToString("D4");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var tmp = filePath + ".tmp";
            File.WriteAllText(tmp, id);
            File.Move(tmp, filePath, overwrite: true);
            log("intake: subject id minted (local fiction — never transmitted)");
        }
        catch (Exception ex)
        {
            log($"intake: subject id persist failed ({ex.GetType().Name}) — in-memory id this session");
        }

        return id;
    }
}
