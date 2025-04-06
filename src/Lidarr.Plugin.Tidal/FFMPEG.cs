using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Plugins;

/// <summary>
/// Provides utilities for interacting with FFMPEG to process audio files.
/// </summary>
internal class FFMPEG
{
    /// <summary>
    /// Probes a media file to determine its audio codecs.
    /// </summary>
    /// <param name="filePath">The path to the media file to probe.</param>
    /// <returns>An array of codec names found in the file.</returns>
    /// <exception cref="FFMPEGException">Thrown when the probe operation fails.</exception>
    public static string[] ProbeCodecs(string filePath)
    {
        var (exitCode, output, errorOutput, a) = Call("ffprobe", $"-select_streams a -show_entries stream=codec_name:stream_tags=language -of default=nk=1:nw=1 {EncodeParameterArgument(filePath)}");
        if (exitCode != 0)
            throw new FFMPEGException($"Probing codecs failed\n{a}\n{errorOutput}");

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Converts a media file to a different format without re-encoding the audio stream.
    /// This preserves the original audio quality while changing the container format.
    /// </summary>
    /// <param name="input">The path to the input file.</param>
    /// <param name="output">The path where the output file should be written.</param>
    /// <exception cref="FFMPEGException">Thrown when the conversion operation fails.</exception>
    public static void ConvertWithoutReencode(string input, string output)
    {
        var (exitCode, _, errorOutput, a) = Call("ffmpeg", $"-y -i {EncodeParameterArgument(input)} -vn -acodec copy {EncodeParameterArgument(output)}");
        if (exitCode != 0)
            throw new FFMPEGException($"Conversion without re-encode failed\n{a}\n{errorOutput}");
    }

    /// <summary>
    /// Re-encodes a media file to a different format with the specified bitrate.
    /// </summary>
    /// <param name="input">The path to the input file.</param>
    /// <param name="output">The path where the output file should be written.</param>
    /// <param name="bitrate">The target bitrate in kilobits per second.</param>
    /// <exception cref="FFMPEGException">Thrown when the re-encoding operation fails.</exception>
    public static void Reencode(string input, string output, int bitrate)
    {
        var (exitCode, _, errorOutput, a) = Call("ffmpeg", $"-y -i {EncodeParameterArgument(input)} -b:a {bitrate}k {EncodeParameterArgument(output)}");
        if (exitCode != 0)
            throw new FFMPEGException($"Re-encoding failed\n{a}\n{errorOutput}");
    }

    /// <summary>
    /// Calls an external process with the specified executable and arguments.
    /// </summary>
    /// <param name="executable">The executable to run (ffmpeg or ffprobe).</param>
    /// <param name="arguments">The command-line arguments to pass to the executable.</param>
    /// <returns>A tuple containing the exit code, standard output, standard error, and the original arguments.</returns>
    private static (int exitCode, string output, string errorOutput, string arg) Call(string executable, string arguments)
    {
        using var proc = new Process()
        {
            StartInfo = new()
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        proc.Start();
        var output = proc.StandardOutput.ReadToEnd();
        var errorOutput = proc.StandardError.ReadToEnd();

        if (!proc.WaitForExit(60000))
            proc.Kill();

        return (proc.ExitCode, output, errorOutput, arguments);
    }

    /// <summary>
    /// Encodes a parameter argument for safe use in command-line arguments.
    /// </summary>
    /// <param name="original">The original parameter value to encode.</param>
    /// <returns>The encoded parameter value.</returns>
    private static string EncodeParameterArgument(string original)
    {
        if (string.IsNullOrEmpty(original))
            return original;

        var value = Regex.Replace(original, @"(\\*)" + "\"", @"$1\$0");
        value = Regex.Replace(value, @"^(.*\s.*?)(\\*)$", "\"$1$2$2\"");
        return value;
    }
}

/// <summary>
/// Exception that is thrown when an FFMPEG operation fails.
/// </summary>
public class FFMPEGException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FFMPEGException"/> class.
    /// </summary>
    public FFMPEGException() { }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="FFMPEGException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public FFMPEGException(string message) : base(message) { }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="FFMPEGException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public FFMPEGException(string message, Exception inner) : base(message, inner) { }
}
