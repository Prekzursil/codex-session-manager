// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using System.Reflection; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
using System.Text;
using CodexSessionManager.Core.Transcripts;

namespace CodexSessionManager.Core.Tests;

public sealed class SessionTranscriptFormatterGuardTests
{
    private static readonly MethodInfo AppendMessageMethod =
        typeof(SessionTranscriptFormatter).GetMethod("AppendMessage", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo TruncateMethod =
        typeof(SessionTranscriptFormatter).GetMethod("Truncate", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void Format_throws_when_session_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => SessionTranscriptFormatter.Format(null!, TranscriptMode.Readable));
    }

    [Fact]
    public void AppendMessage_throws_when_builder_is_null()
    {
        var message = NormalizedSessionEvent.CreateMessage(SessionActor.User, "hello");

        var exception = Assert.Throws<TargetInvocationException>(() =>
            AppendMessageMethod.Invoke(null, [message, TranscriptMode.Readable, null!]));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void Truncate_throws_when_value_is_null()
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
            TruncateMethod.Invoke(null, [null!, 20]));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }
}

