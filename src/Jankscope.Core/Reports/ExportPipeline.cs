using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jankscope.Core.Reports;

/// <summary>
/// What will be written, exactly: the bytes, the redaction that produced them, and whether redaction was applied at all. The only way to get one is
/// <see cref="ExportPipeline.Prepare"/>, which has already run the guard; <see cref="ExportPipeline.WriteAtomic"/> writes these bytes and nothing else,
/// so the preview a person reads is the file they get.
/// </summary>
public sealed class ExportPreview
{
    internal ExportPreview(string text, IReadOnlyDictionary<RedactionCategory, int> counts, bool redactionApplied)
    {
        Text = text;
        Bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        Counts = counts;
        RedactionApplied = redactionApplied;
        Sha256 = Convert.ToHexString(SHA256.HashData(Bytes.Span));
    }

    /// <summary>The document as it will be written (UTF-8, no byte order mark).</summary>
    public string Text { get; }

    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Replacements made, per category (every category is present).</summary>
    public IReadOnlyDictionary<RedactionCategory, int> Counts { get; }

    public bool RedactionApplied { get; }

    /// <summary>SHA-256 of <see cref="Bytes"/>, upper-case hex, so the person can see the file on disk is the file previewed.</summary>
    public string Sha256 { get; }

    public int TotalReplacements => Counts.Values.Sum();
}

/// <summary>Builds a redacted, guarded export from any serializable model, and writes it.</summary>
public static class ExportPipeline
{
    /// <summary>
    /// Serializes <paramref name="model"/> with the product's JSON options, redacts every string and property name in the tree (when
    /// <paramref name="redact"/>), adds a <c>redaction</c> block saying what was done, and runs <see cref="ExportGuard"/>.
    /// Throws <see cref="ExportBlockedException"/> when anything known to be sensitive survives; no preview exists then.
    /// </summary>
    /// <param name="context">What the redactor knows.</param>
    /// <param name="redact">False only when the person explicitly chose an unredacted export; the file then says so.</param>
    /// <param name="guardContext">
    /// What the guard looks for, when that is more than the redactor knows: planted canaries, or values another part of the program learned.
    /// Defaults to <paramref name="context"/>.
    /// </param>
    public static ExportPreview Prepare(object model, RedactionContext context, bool redact = true, RedactionContext? guardContext = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);

        var node = JsonSerializer.SerializeToNode(model, JankscopeJson.Options)
            ?? throw new ArgumentException("The model serialized to nothing.", nameof(model));
        var redactor = new Redactor(context);
        var counts = redactor.Counts;

        if (redact)
        {
            node = redactor.RedactTree(node)!;
        }

        if (node is JsonObject root)
        {
            var block = new JsonObject { ["applied"] = redact };
            if (redact)
            {
                var replacements = new JsonObject();
                foreach (var category in Enum.GetValues<RedactionCategory>())
                {
                    if (counts[category] > 0)
                    {
                        replacements[category.ToString()] = counts[category];
                    }
                }

                block["replacements"] = replacements;
            }
            else
            {
                block["warning"] = "This export was made with redaction switched off and may contain personal data.";
            }

            root["redaction"] = block;
        }

        var text = node.ToJsonString(JankscopeJson.Options);
        if (redact)
        {
            ExportGuard.Verify(text, guardContext ?? context);
        }

        return new ExportPreview(text, new Dictionary<RedactionCategory, int>(counts), redact);
    }

    /// <summary>
    /// Writes exactly <see cref="ExportPreview.Bytes"/> to <paramref name="path"/> through a temporary file in the same folder, so a crash never leaves
    /// half a report. Throws <see cref="IOException"/> if the file exists and <paramref name="overwrite"/> is false.
    /// </summary>
    public static void WriteAtomic(ExportPreview preview, string path, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? throw new IOException("The path has no folder.");
        if (!overwrite && File.Exists(full))
        {
            throw new IOException("The file already exists; choose another name.");
        }

        var temporary = Path.Combine(directory, Path.GetFileName(full) + ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(preview.Bytes.Span);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, full, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
