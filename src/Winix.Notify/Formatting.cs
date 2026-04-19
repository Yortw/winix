#nullable enable
using System.Collections.Generic;
using Yort.ShellKit;

namespace Winix.Notify;

/// <summary>JSON output composition for <c>--json</c> mode. Pure — no I/O.</summary>
public static class Formatting
{
    /// <summary>Compose the JSON document describing what was sent and the per-backend status.</summary>
    public static string Json(NotifyOptions options, IReadOnlyList<BackendResult> results)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("title", options.Title);
            if (options.Body is not null)
            {
                writer.WriteString("body", options.Body);
            }
            writer.WriteString("urgency", options.Urgency switch
            {
                Urgency.Low => "low",
                Urgency.Critical => "critical",
                _ => "normal",
            });
            writer.WriteStartArray("backends");
            foreach (var r in results)
            {
                writer.WriteStartObject();
                writer.WriteString("name", r.BackendName);
                writer.WriteBoolean("ok", r.Ok);
                if (r.Error is not null)
                {
                    writer.WriteString("error", r.Error);
                }
                if (r.Detail is not null)
                {
                    foreach (var kv in r.Detail)
                    {
                        writer.WriteString(kv.Key, kv.Value);
                    }
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }
}
