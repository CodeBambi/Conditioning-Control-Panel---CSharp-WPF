using System;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Tracks one Deeper enhancement the user has submitted to the catalogue so
    /// the app can surface acceptance/publication feedback after the otherwise
    /// fire-and-forget submit: a status badge on the library row + a one-time
    /// "published" notification on the next status poll.
    ///
    /// Persisted in AppSettings.DeeperSubmissions, keyed by the canonical
    /// .ccpenh.json file path. Kept here (not in the bundle metadata) so a
    /// status refresh never has to rewrite — and bump the mtime of — the user's
    /// file, and so re-submission never ships our local tracking to the server.
    /// </summary>
    public class DeeperSubmissionRecord
    {
        // Server-side submission id, returned by POST /api/enhancements (201) or
        // the existing_id of a 409 duplicate. The stable correlation key used to
        // reconcile against GET /api/enhancements/mine.
        [JsonProperty("catalogue_id")]
        public string CatalogueId { get; set; } = "";

        // Last-known server status: "pending" | "approved" | "published" | "rejected".
        [JsonProperty("status")]
        public string Status { get; set; } = "pending";

        [JsonProperty("submitted_utc")]
        public DateTime SubmittedUtc { get; set; }

        [JsonProperty("last_checked_utc")]
        public DateTime LastCheckedUtc { get; set; }

        // True once the "accepted/published" notification has fired, so repeated
        // status polls don't re-toast on every launch.
        [JsonProperty("accepted_notified")]
        public bool AcceptedNotified { get; set; }
    }
}
