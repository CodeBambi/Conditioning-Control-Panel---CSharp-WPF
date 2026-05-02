using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Embeds Deeper enhancement JSON into local audio/video files as native
    /// container metadata. The output file plays normally in any standard
    /// player (VLC, WMP, foobar2000, etc.) — they see the metadata as an
    /// unknown atom/frame/chunk and skip it. CCP's player scans for the
    /// embedded JSON on import and loads it automatically.
    ///
    /// Per-format mechanism:
    ///   ISOBMFF (.mp4/.m4a/.m4v/.mov)  — append top-level "uuid" box at EOF
    ///   MP3                            — add/replace ID3v2 TXXX frame at start
    ///   WAV                            — append top-level "ccpe" RIFF chunk
    ///
    /// Magic identifiers are stable and MUST NOT change once shipped — they
    /// are how the import side recognises a CCP-enhanced file.
    /// </summary>
    public static class EnhancementMediaBundler
    {
        // Stable magic identifiers — DO NOT CHANGE.
        // ASCII "CCPENHANCE-JSON" packed as a 16-byte UUID for the ISOBMFF uuid box.
        private static readonly byte[] IsoBmffUuid = new byte[]
        {
            0x43, 0x43, 0x50, 0x45, // C C P E
            0x4E, 0x48, 0x41, 0x4E, // N H A N
            0x43, 0x45, 0x2D, 0x4A, // C E - J
            0x53, 0x4F, 0x4E, 0x21  // S O N !
        };
        private const string Id3TxxxDescription = "CCP_ENHANCEMENT_V1";
        private const uint WavCcpeFourcc = 0x65706363; // "ccpe" little-endian

        // Hard caps to keep allocations bounded when reading hostile files.
        private const long MaxMediaFileSize = 4L * 1024 * 1024 * 1024; // 4 GB
        private const int MaxIsoBmffTailScan = 8 * 1024 * 1024;        // 8 MB tail scan
        private const int MaxId3TagSize = 16 * 1024 * 1024;            // 16 MB
        private const int MaxJsonPayload = 2_000_000;                  // 2 MB extracted JSON cap (vs 1 MB on save)

        public sealed class BundleResult
        {
            public bool Success { get; init; }
            public string? Error { get; init; }
            public string? OutputPath { get; init; }
            public static BundleResult Ok(string path) => new() { Success = true, OutputPath = path };
            public static BundleResult Fail(string error) => new() { Success = false, Error = error };
        }

        // -- Public surface ----------------------------------------------------

        public static bool IsSupportedExtension(string path)
        {
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return ext switch
            {
                ".mp4" or ".m4a" or ".m4v" or ".mov" => true,
                ".mp3" => true,
                ".wav" => true,
                _ => false,
            };
        }

        /// <summary>
        /// Copies <paramref name="sourceMediaPath"/> to <paramref name="destPath"/>,
        /// then injects the enhancement JSON into the destination file using the
        /// container-appropriate mechanism. Source is never modified.
        /// </summary>
        public static BundleResult Export(Enhancement enhancement, string sourceMediaPath, string destPath)
        {
            if (enhancement == null) return BundleResult.Fail("Enhancement is null.");
            if (string.IsNullOrEmpty(sourceMediaPath) || !File.Exists(sourceMediaPath))
                return BundleResult.Fail($"Source file not found: {sourceMediaPath}");
            if (string.IsNullOrEmpty(destPath))
                return BundleResult.Fail("Destination path is empty.");
            if (!IsSupportedExtension(sourceMediaPath))
                return BundleResult.Fail($"Unsupported source format: {Path.GetExtension(sourceMediaPath)}");
            if (!IsSupportedExtension(destPath))
                return BundleResult.Fail($"Unsupported destination format: {Path.GetExtension(destPath)}");

            var srcInfo = new FileInfo(sourceMediaPath);
            if (srcInfo.Length > MaxMediaFileSize)
                return BundleResult.Fail($"Source file is too large ({srcInfo.Length} bytes).");

            string json;
            try
            {
                json = EnhancementSerializer.Save(enhancement);
            }
            catch (Exception ex)
            {
                return BundleResult.Fail($"Failed to serialize enhancement: {ex.Message}");
            }

            try
            {
                // Copy first, then inject in-place. If injection fails, we
                // delete the partial dest so the user isn't left with a
                // half-baked file masquerading as enhanced.
                File.Copy(sourceMediaPath, destPath, overwrite: true);
            }
            catch (Exception ex)
            {
                return BundleResult.Fail($"Failed to copy source: {ex.Message}");
            }

            try
            {
                var ext = Path.GetExtension(destPath).ToLowerInvariant();
                switch (ext)
                {
                    case ".mp4":
                    case ".m4a":
                    case ".m4v":
                    case ".mov":
                        WriteIsoBmffUuidBox(destPath, json);
                        break;
                    case ".mp3":
                        WriteId3v2TxxxFrame(destPath, json);
                        break;
                    case ".wav":
                        WriteWavCcpeChunk(destPath, json);
                        break;
                    default:
                        throw new InvalidOperationException($"Unhandled extension: {ext}");
                }
                return BundleResult.Ok(destPath);
            }
            catch (Exception ex)
            {
                try { File.Delete(destPath); } catch { /* best effort */ }
                App.Logger?.Warning(ex, "EnhancementMediaBundler.Export injection failed: {Path}", destPath);
                return BundleResult.Fail($"Failed to inject metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans <paramref name="mediaPath"/> for embedded CCP enhancement
        /// metadata. Returns true on found+parsed; false on absent or
        /// malformed. Never throws on routine misses (returning false is the
        /// happy path for a clean unbundled file).
        /// </summary>
        public static bool TryExtract(string mediaPath, out Enhancement? enhancement, out string? error)
        {
            enhancement = null;
            error = null;
            try
            {
                if (string.IsNullOrEmpty(mediaPath) || !File.Exists(mediaPath))
                {
                    error = "File not found.";
                    return false;
                }
                if (!IsSupportedExtension(mediaPath)) return false;

                var info = new FileInfo(mediaPath);
                if (info.Length > MaxMediaFileSize)
                {
                    error = $"File too large ({info.Length} bytes).";
                    return false;
                }

                string? json;
                var ext = Path.GetExtension(mediaPath).ToLowerInvariant();
                switch (ext)
                {
                    case ".mp4":
                    case ".m4a":
                    case ".m4v":
                    case ".mov":
                        json = ReadIsoBmffUuidBox(mediaPath);
                        break;
                    case ".mp3":
                        json = ReadId3v2TxxxFrame(mediaPath);
                        break;
                    case ".wav":
                        json = ReadWavCcpeChunk(mediaPath);
                        break;
                    default:
                        return false;
                }

                if (string.IsNullOrEmpty(json)) return false;
                if (json!.Length > MaxJsonPayload)
                {
                    error = $"Embedded JSON payload too large ({json.Length} bytes).";
                    return false;
                }

                enhancement = EnhancementSerializer.Load(json);
                return true;
            }
            catch (EnhancementLoadException ex)
            {
                error = ex.Message;
                App.Logger?.Debug("EnhancementMediaBundler.TryExtract: schema/parse error: {Error}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                App.Logger?.Debug("EnhancementMediaBundler.TryExtract failed: {Error}", ex.Message);
                return false;
            }
        }

        // -- ISOBMFF (MP4/M4A/M4V/MOV): top-level "uuid" box ------------------

        private static void WriteIsoBmffUuidBox(string path, string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);

            // First, strip any pre-existing CCP uuid box(es) so re-export
            // doesn't accumulate stale metadata. Walk top-level boxes; when we
            // find a uuid box with our magic, truncate the file at that
            // offset (only safe at EOF; if it isn't at EOF we just leave it
            // and the new box wins on read since we scan tail-first).
            StripTrailingIsoBmffUuidBox(path);

            // size = 8 (header) + 16 (uuid) + payload
            long boxLen = 8L + 16L + payload.Length;
            byte[] header;
            if (boxLen <= uint.MaxValue)
            {
                header = new byte[8 + 16];
                WriteUInt32BE(header, 0, (uint)boxLen);
                header[4] = (byte)'u'; header[5] = (byte)'u';
                header[6] = (byte)'i'; header[7] = (byte)'d';
                Buffer.BlockCopy(IsoBmffUuid, 0, header, 8, 16);
            }
            else
            {
                // Use 64-bit largesize variant: size field = 1, then 8-byte largesize.
                long fullLen = boxLen + 8;
                header = new byte[16 + 16];
                WriteUInt32BE(header, 0, 1u);
                header[4] = (byte)'u'; header[5] = (byte)'u';
                header[6] = (byte)'i'; header[7] = (byte)'d';
                WriteUInt64BE(header, 8, (ulong)fullLen);
                Buffer.BlockCopy(IsoBmffUuid, 0, header, 16, 16);
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            fs.Seek(0, SeekOrigin.End);
            fs.Write(header, 0, header.Length);
            fs.Write(payload, 0, payload.Length);
        }

        private static void StripTrailingIsoBmffUuidBox(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            long fileLen = fs.Length;
            long pos = 0;
            long lastCcpBoxStart = -1;

            while (pos + 8 <= fileLen)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                var hdr = new byte[8];
                if (fs.Read(hdr, 0, 8) != 8) break;
                long size = ReadUInt32BE(hdr, 0);
                var type = Encoding.ASCII.GetString(hdr, 4, 4);
                long boxStart = pos;
                long headerSize = 8;

                if (size == 1)
                {
                    // 64-bit largesize follows.
                    var ls = new byte[8];
                    if (fs.Read(ls, 0, 8) != 8) break;
                    size = (long)ReadUInt64BE(ls, 0);
                    headerSize = 16;
                }
                else if (size == 0)
                {
                    // Box extends to EOF.
                    size = fileLen - boxStart;
                }

                if (size < headerSize || boxStart + size > fileLen) break;

                if (type == "uuid" && size >= headerSize + 16)
                {
                    var u = new byte[16];
                    if (fs.Read(u, 0, 16) == 16 && BytesEqual(u, IsoBmffUuid))
                    {
                        lastCcpBoxStart = boxStart;
                        // Don't break — there could be multiple stale boxes;
                        // we want the earliest one that's still trailing.
                    }
                }

                pos = boxStart + size;
            }

            // Only truncate if our box is the last thing in the file (i.e. no
            // other top-level boxes follow it). Walking pos == fileLen at the
            // end of the loop confirms a clean parse.
            if (lastCcpBoxStart >= 0 && pos == fileLen)
            {
                fs.SetLength(lastCcpBoxStart);
            }
        }

        private static string? ReadIsoBmffUuidBox(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long fileLen = fs.Length;

            // Tail-first: we always APPEND, so the box is at the end. Read up
            // to MaxIsoBmffTailScan bytes from the tail and look for the
            // magic UUID. If found, parse the surrounding box header.
            long scanLen = Math.Min(MaxIsoBmffTailScan, fileLen);
            long scanStart = fileLen - scanLen;
            fs.Seek(scanStart, SeekOrigin.Begin);
            var tail = new byte[scanLen];
            int read = 0;
            while (read < tail.Length)
            {
                int n = fs.Read(tail, read, tail.Length - read);
                if (n <= 0) break;
                read += n;
            }

            // Find the magic UUID in the tail buffer.
            int idx = IndexOf(tail, IsoBmffUuid, 0);
            while (idx >= 0)
            {
                // Box header is 8 bytes (or 16 for largesize) before the UUID
                // payload. So the UUID starts at boxStart + 8 (normal) or
                // boxStart + 16 (largesize).
                long uuidFileOffset = scanStart + idx;

                // Try 8-byte header first (most common).
                long boxStart = uuidFileOffset - 8;
                if (boxStart >= scanStart)
                {
                    int rel = (int)(boxStart - scanStart);
                    long size = ReadUInt32BE(tail, rel);
                    var type = Encoding.ASCII.GetString(tail, rel + 4, 4);
                    if (type == "uuid" && size != 1 && size >= 24)
                    {
                        long payloadStart = boxStart + 24;
                        long payloadLen = size - 24;
                        if (payloadLen >= 0 && payloadStart + payloadLen <= fileLen)
                        {
                            return ReadFileSlice(fs, payloadStart, payloadLen);
                        }
                    }
                }

                // Try 16-byte largesize header.
                long boxStartLs = uuidFileOffset - 16;
                if (boxStartLs >= scanStart)
                {
                    int rel = (int)(boxStartLs - scanStart);
                    long sizeField = ReadUInt32BE(tail, rel);
                    var typeLs = Encoding.ASCII.GetString(tail, rel + 4, 4);
                    if (sizeField == 1 && typeLs == "uuid")
                    {
                        long size = (long)ReadUInt64BE(tail, rel + 8);
                        if (size >= 32)
                        {
                            long payloadStart = boxStartLs + 32;
                            long payloadLen = size - 32;
                            if (payloadLen >= 0 && payloadStart + payloadLen <= fileLen)
                            {
                                return ReadFileSlice(fs, payloadStart, payloadLen);
                            }
                        }
                    }
                }

                idx = IndexOf(tail, IsoBmffUuid, idx + 1);
            }

            return null;
        }

        // -- MP3: ID3v2.4 TXXX frame -----------------------------------------

        private static void WriteId3v2TxxxFrame(string path, string json)
        {
            // Read existing tag (if any) + audio body. Rewrite tag with our
            // TXXX frame replaced/added, then concat audio body.
            byte[] existingTag;
            byte[] audioBody;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                ReadId3v2TagAndBody(fs, out existingTag, out audioBody);
            }

            var frames = ParseId3v2Frames(existingTag);
            // Drop any pre-existing CCP TXXX frame so re-export doesn't dupe.
            frames.RemoveAll(f => IsOurTxxxFrame(f));

            // Build our TXXX frame.
            // Frame body: [encoding:1=03 UTF-8][description UTF-8 + 00][value UTF-8]
            var descBytes = Encoding.UTF8.GetBytes(Id3TxxxDescription);
            var valBytes = Encoding.UTF8.GetBytes(json);
            var frameBody = new byte[1 + descBytes.Length + 1 + valBytes.Length];
            frameBody[0] = 0x03; // UTF-8
            Buffer.BlockCopy(descBytes, 0, frameBody, 1, descBytes.Length);
            // descBytes terminator already 0x00 from default array init.
            Buffer.BlockCopy(valBytes, 0, frameBody, 1 + descBytes.Length + 1, valBytes.Length);

            var ourFrame = new Id3Frame { Id = "TXXX", Flags = 0, Body = frameBody };
            frames.Add(ourFrame);

            var newTag = SerializeId3v2Tag(frames);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(newTag, 0, newTag.Length);
                fs.Write(audioBody, 0, audioBody.Length);
            }
        }

        private static string? ReadId3v2TxxxFrame(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ReadId3v2TagAndBody(fs, out var tag, out _);
            if (tag.Length == 0) return null;

            var frames = ParseId3v2Frames(tag);
            var ours = frames.FirstOrDefault(IsOurTxxxFrame);
            if (ours == null) return null;

            // Body: [encoding:1][description + null terminator(s)][value]
            if (ours.Body.Length < 2) return null;
            byte enc = ours.Body[0];
            int descStart = 1;
            int descEnd;
            int valStart;

            if (enc == 0x01 || enc == 0x02)
            {
                // UTF-16 (BOM) or UTF-16BE. Find double-null terminator.
                descEnd = descStart;
                while (descEnd + 1 < ours.Body.Length && !(ours.Body[descEnd] == 0 && ours.Body[descEnd + 1] == 0))
                    descEnd += 2;
                valStart = descEnd + 2;
            }
            else
            {
                // 0x00 ISO-8859-1, 0x03 UTF-8 — both single-null terminated.
                descEnd = descStart;
                while (descEnd < ours.Body.Length && ours.Body[descEnd] != 0) descEnd++;
                valStart = descEnd + 1;
            }

            if (valStart > ours.Body.Length) return null;
            int valLen = ours.Body.Length - valStart;
            return enc switch
            {
                0x00 => Encoding.GetEncoding("ISO-8859-1").GetString(ours.Body, valStart, valLen),
                0x01 => DecodeUtf16WithBom(ours.Body, valStart, valLen),
                0x02 => Encoding.BigEndianUnicode.GetString(ours.Body, valStart, valLen),
                0x03 => Encoding.UTF8.GetString(ours.Body, valStart, valLen),
                _ => Encoding.UTF8.GetString(ours.Body, valStart, valLen),
            };
        }

        private static bool IsOurTxxxFrame(Id3Frame f)
        {
            if (f.Id != "TXXX" || f.Body.Length < 2) return false;
            byte enc = f.Body[0];
            string desc;
            try
            {
                int end = 1;
                if (enc == 0x01 || enc == 0x02)
                {
                    while (end + 1 < f.Body.Length && !(f.Body[end] == 0 && f.Body[end + 1] == 0)) end += 2;
                    int descLen = end - 1;
                    desc = enc == 0x01
                        ? DecodeUtf16WithBom(f.Body, 1, descLen)
                        : Encoding.BigEndianUnicode.GetString(f.Body, 1, descLen);
                }
                else
                {
                    while (end < f.Body.Length && f.Body[end] != 0) end++;
                    int descLen = end - 1;
                    desc = enc == 0x03
                        ? Encoding.UTF8.GetString(f.Body, 1, descLen)
                        : Encoding.GetEncoding("ISO-8859-1").GetString(f.Body, 1, descLen);
                }
            }
            catch { return false; }
            return desc == Id3TxxxDescription;
        }

        private sealed class Id3Frame
        {
            public string Id { get; set; } = "";
            public ushort Flags { get; set; }
            public byte[] Body { get; set; } = Array.Empty<byte>();
        }

        private static void ReadId3v2TagAndBody(FileStream fs, out byte[] tag, out byte[] body)
        {
            fs.Seek(0, SeekOrigin.Begin);
            var hdr = new byte[10];
            int n = fs.Read(hdr, 0, 10);
            if (n < 10 || hdr[0] != (byte)'I' || hdr[1] != (byte)'D' || hdr[2] != (byte)'3')
            {
                // No ID3v2 tag — entire file is audio body.
                tag = Array.Empty<byte>();
                fs.Seek(0, SeekOrigin.Begin);
                body = ReadAll(fs);
                return;
            }
            int tagSize = ReadSynchsafe(hdr, 6);
            if (tagSize < 0 || tagSize > MaxId3TagSize)
                throw new InvalidDataException($"ID3v2 tag size out of range: {tagSize}");
            tag = new byte[10 + tagSize];
            Buffer.BlockCopy(hdr, 0, tag, 0, 10);
            int read = 0;
            while (read < tagSize)
            {
                int got = fs.Read(tag, 10 + read, tagSize - read);
                if (got <= 0) throw new EndOfStreamException("Truncated ID3v2 tag.");
                read += got;
            }
            body = ReadAll(fs);
        }

        private static List<Id3Frame> ParseId3v2Frames(byte[] tag)
        {
            var result = new List<Id3Frame>();
            if (tag.Length < 10) return result;
            byte version = tag[3]; // major version (3 or 4)
            byte tagFlags = tag[5];
            bool unsync = (tagFlags & 0x80) != 0;
            // We don't apply tag-level unsynchronisation to frames here — it's
            // rare in the wild and our own files don't use it. If we
            // encounter it we just log and bail; the file gets a fresh tag.
            if (unsync)
            {
                App.Logger?.Debug("ID3v2 tag uses unsynchronisation — frames not parsed.");
                return result;
            }
            int tagSize = ReadSynchsafe(tag, 6);
            int pos = 10;
            // Skip extended header if present.
            if ((tagFlags & 0x40) != 0 && pos + 4 <= tag.Length)
            {
                int extSize = version >= 4 ? ReadSynchsafe(tag, pos) : (int)ReadUInt32BE(tag, pos);
                pos += extSize;
            }
            int end = Math.Min(tag.Length, 10 + tagSize);
            while (pos + 10 <= end)
            {
                // Padding starts when frame ID is all zeros.
                if (tag[pos] == 0) break;
                string id = Encoding.ASCII.GetString(tag, pos, 4);
                int frameSize = version >= 4 ? ReadSynchsafe(tag, pos + 4) : (int)ReadUInt32BE(tag, pos + 4);
                ushort flags = (ushort)((tag[pos + 8] << 8) | tag[pos + 9]);
                pos += 10;
                if (frameSize < 0 || pos + frameSize > end) break;
                var body = new byte[frameSize];
                Buffer.BlockCopy(tag, pos, body, 0, frameSize);
                result.Add(new Id3Frame { Id = id, Flags = flags, Body = body });
                pos += frameSize;
            }
            return result;
        }

        private static byte[] SerializeId3v2Tag(List<Id3Frame> frames)
        {
            // Write ID3v2.4, no extended header, no unsync.
            using var ms = new MemoryStream();
            foreach (var f in frames)
            {
                if (f.Id.Length != 4) continue;
                ms.Write(Encoding.ASCII.GetBytes(f.Id), 0, 4);
                var sz = new byte[4];
                WriteSynchsafe(sz, 0, f.Body.Length);
                ms.Write(sz, 0, 4);
                ms.WriteByte((byte)((f.Flags >> 8) & 0xFF));
                ms.WriteByte((byte)(f.Flags & 0xFF));
                ms.Write(f.Body, 0, f.Body.Length);
            }
            var framesData = ms.ToArray();

            var tag = new byte[10 + framesData.Length];
            tag[0] = (byte)'I'; tag[1] = (byte)'D'; tag[2] = (byte)'3';
            tag[3] = 4; // version 2.4
            tag[4] = 0; // revision
            tag[5] = 0; // flags
            WriteSynchsafe(tag, 6, framesData.Length);
            Buffer.BlockCopy(framesData, 0, tag, 10, framesData.Length);
            return tag;
        }

        // -- WAV: append "ccpe" RIFF chunk -----------------------------------

        private static void WriteWavCcpeChunk(string path, string json)
        {
            // Strip any pre-existing trailing ccpe chunk so re-export is idempotent.
            StripTrailingWavCcpeChunk(path);

            var payload = Encoding.UTF8.GetBytes(json);
            // Chunks must be word-aligned; pad with one zero byte if odd.
            bool needsPad = (payload.Length & 1) == 1;
            int chunkLenIncludingHeader = 8 + payload.Length + (needsPad ? 1 : 0);

            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            // Validate RIFF header.
            var hdr = new byte[12];
            if (fs.Length < 12 || fs.Read(hdr, 0, 12) != 12)
                throw new InvalidDataException("WAV file too short.");
            if (hdr[0] != (byte)'R' || hdr[1] != (byte)'I' || hdr[2] != (byte)'F' || hdr[3] != (byte)'F')
                throw new InvalidDataException("Not a RIFF file.");
            if (hdr[8] != (byte)'W' || hdr[9] != (byte)'A' || hdr[10] != (byte)'V' || hdr[11] != (byte)'E')
                throw new InvalidDataException("Not a WAVE RIFF.");

            uint oldRiffSize = ReadUInt32LE(hdr, 4);
            uint newRiffSize = checked(oldRiffSize + (uint)chunkLenIncludingHeader);

            // Append chunk at EOF.
            fs.Seek(0, SeekOrigin.End);
            var chunkHeader = new byte[8];
            WriteUInt32LE(chunkHeader, 0, WavCcpeFourcc);
            WriteUInt32LE(chunkHeader, 4, (uint)payload.Length);
            fs.Write(chunkHeader, 0, 8);
            fs.Write(payload, 0, payload.Length);
            if (needsPad) fs.WriteByte(0);

            // Patch master RIFF size.
            fs.Seek(4, SeekOrigin.Begin);
            var sz = new byte[4];
            WriteUInt32LE(sz, 0, newRiffSize);
            fs.Write(sz, 0, 4);
        }

        private static void StripTrailingWavCcpeChunk(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (fs.Length < 12) return;
            var hdr = new byte[12];
            if (fs.Read(hdr, 0, 12) != 12) return;
            if (hdr[0] != (byte)'R' || hdr[1] != (byte)'I' || hdr[2] != (byte)'F' || hdr[3] != (byte)'F') return;

            long pos = 12;
            long fileLen = fs.Length;
            long lastCcpStart = -1;
            long lastCcpEnd = -1;

            while (pos + 8 <= fileLen)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                var ch = new byte[8];
                if (fs.Read(ch, 0, 8) != 8) break;
                uint fourcc = ReadUInt32LE(ch, 0);
                uint chSize = ReadUInt32LE(ch, 4);
                long chStart = pos;
                long bodyStart = pos + 8;
                long bodyEnd = bodyStart + chSize;
                long padded = bodyEnd + ((chSize & 1) == 1 ? 1 : 0);
                if (bodyEnd > fileLen) break;

                if (fourcc == WavCcpeFourcc)
                {
                    lastCcpStart = chStart;
                    lastCcpEnd = padded;
                }
                pos = padded;
            }

            // Truncate only if our chunk was the last thing in the file.
            if (lastCcpStart >= 0 && pos == fileLen && lastCcpEnd == fileLen)
            {
                fs.SetLength(lastCcpStart);
                // Patch RIFF size back down.
                long shrunkBy = fileLen - lastCcpStart;
                fs.Seek(4, SeekOrigin.Begin);
                var oldSz = new byte[4];
                fs.Read(oldSz, 0, 4);
                uint old = ReadUInt32LE(oldSz, 0);
                uint @new = (uint)Math.Max(0, (long)old - shrunkBy);
                fs.Seek(4, SeekOrigin.Begin);
                var nb = new byte[4];
                WriteUInt32LE(nb, 0, @new);
                fs.Write(nb, 0, 4);
            }
        }

        private static string? ReadWavCcpeChunk(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 12) return null;
            var hdr = new byte[12];
            if (fs.Read(hdr, 0, 12) != 12) return null;
            if (hdr[0] != (byte)'R' || hdr[1] != (byte)'I' || hdr[2] != (byte)'F' || hdr[3] != (byte)'F') return null;
            if (hdr[8] != (byte)'W' || hdr[9] != (byte)'A' || hdr[10] != (byte)'V' || hdr[11] != (byte)'E') return null;

            long pos = 12;
            long fileLen = fs.Length;
            string? found = null;
            while (pos + 8 <= fileLen)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                var ch = new byte[8];
                if (fs.Read(ch, 0, 8) != 8) break;
                uint fourcc = ReadUInt32LE(ch, 0);
                uint chSize = ReadUInt32LE(ch, 4);
                long bodyStart = pos + 8;
                if (bodyStart + chSize > fileLen) break;

                if (fourcc == WavCcpeFourcc)
                {
                    var body = new byte[chSize];
                    int read = 0;
                    while (read < body.Length)
                    {
                        int got = fs.Read(body, read, body.Length - read);
                        if (got <= 0) break;
                        read += got;
                    }
                    found = Encoding.UTF8.GetString(body);
                    // Don't break — last one wins (mirror append-on-write semantics).
                }
                pos = bodyStart + chSize + ((chSize & 1) == 1 ? 1 : 0);
            }
            return found;
        }

        // -- Helpers ----------------------------------------------------------

        private static byte[] ReadAll(FileStream fs)
        {
            using var ms = new MemoryStream();
            var buf = new byte[64 * 1024];
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
            return ms.ToArray();
        }

        private static string ReadFileSlice(FileStream fs, long start, long length)
        {
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[length];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return Encoding.UTF8.GetString(buf, 0, read);
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            if (needle.Length == 0) return start;
            int last = haystack.Length - needle.Length;
            for (int i = start; i <= last; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static uint ReadUInt32BE(byte[] b, int o) =>
            ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

        private static ulong ReadUInt64BE(byte[] b, int o) =>
            ((ulong)ReadUInt32BE(b, o) << 32) | ReadUInt32BE(b, o + 4);

        private static void WriteUInt32BE(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        private static void WriteUInt64BE(byte[] b, int o, ulong v)
        {
            WriteUInt32BE(b, o, (uint)(v >> 32));
            WriteUInt32BE(b, o + 4, (uint)v);
        }

        private static uint ReadUInt32LE(byte[] b, int o) =>
            b[o] | ((uint)b[o + 1] << 8) | ((uint)b[o + 2] << 16) | ((uint)b[o + 3] << 24);

        private static void WriteUInt32LE(byte[] b, int o, uint v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }

        private static int ReadSynchsafe(byte[] b, int o)
        {
            // 28-bit synchsafe (each byte uses only low 7 bits).
            return ((b[o] & 0x7F) << 21) | ((b[o + 1] & 0x7F) << 14)
                 | ((b[o + 2] & 0x7F) << 7) | (b[o + 3] & 0x7F);
        }

        private static void WriteSynchsafe(byte[] b, int o, int v)
        {
            b[o]     = (byte)((v >> 21) & 0x7F);
            b[o + 1] = (byte)((v >> 14) & 0x7F);
            b[o + 2] = (byte)((v >> 7)  & 0x7F);
            b[o + 3] = (byte)( v        & 0x7F);
        }

        private static string DecodeUtf16WithBom(byte[] b, int o, int len)
        {
            if (len >= 2 && b[o] == 0xFF && b[o + 1] == 0xFE)
                return Encoding.Unicode.GetString(b, o + 2, len - 2);
            if (len >= 2 && b[o] == 0xFE && b[o + 1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(b, o + 2, len - 2);
            return Encoding.Unicode.GetString(b, o, len);
        }
    }
}
