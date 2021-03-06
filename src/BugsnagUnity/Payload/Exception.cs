using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BugsnagUnity.Payload
{
  /// <summary>
  /// Represents a set of Bugsnag payload exceptions that are generated from a single exception by resolving
  /// the inner exceptions present.
  /// </summary>
  class Exceptions : IEnumerable<Exception>
  {
    private IEnumerable<Exception> UnwoundExceptions { get; }

    internal Exceptions(System.Exception exception, System.Diagnostics.StackFrame[] alternativeStackTrace)
    {
      UnwoundExceptions = FlattenAndReverseExceptionTree(exception).Select(e => Exception.FromSystemException(e, alternativeStackTrace));
    }

    public IEnumerator<Exception> GetEnumerator()
    {
      return UnwoundExceptions.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }

    private static IEnumerable<System.Exception> FlattenAndReverseExceptionTree(System.Exception ex)
    {
      if (ex == null) yield break;

      yield return ex;

      switch (ex)
      {
        case ReflectionTypeLoadException typeLoadException:
          foreach (var exception in typeLoadException.LoaderExceptions)
          {
            foreach (var item in FlattenAndReverseExceptionTree(exception))
            {
              yield return item;
            }
          }
          break;
        default:
          foreach (var item in FlattenAndReverseExceptionTree(ex.InnerException))
          {
            yield return item;
          }
          break;
      }
    }
  }

  /// <summary>
  /// Represents an individual exception in the Bugsnag payload.
  /// </summary>
  public class Exception : Dictionary<string, object>
  {
    internal HandledState HandledState { get; }

    private static string AndroidJavaErrorClass = "AndroidJavaException";
    private static string ErrorClassMessagePattern = @"^(?<errorClass>\S+):\s*(?<message>.*)";
    private static string BugsnagStackTraceMarker = "libbugsnag";

    internal Exception(string errorClass, string message, StackTraceLine[] stackTrace)
      : this(errorClass, message, stackTrace, HandledState.ForHandledException()) {}

    internal Exception(string errorClass, string message, StackTraceLine[] stackTrace, HandledState handledState)
    {
      this.AddToPayload("errorClass", errorClass);
      this.AddToPayload("message", message);
      this.AddToPayload("stacktrace", stackTrace);
      HandledState = handledState;
    }

    public IEnumerable<StackTraceLine> StackTrace { get { return this.Get("stacktrace") as IEnumerable<StackTraceLine>; } }

    public string ErrorClass
    {
      get => this.Get("errorClass") as string;
      set => this.AddToPayload("errorClass", value);
    }

    public string ErrorMessage
    {
      get => this.Get("message") as string;
      set => this.AddToPayload("message", value);
    }

    internal static Exception FromSystemException(System.Exception exception, System.Diagnostics.StackFrame[] alternativeStackTrace)
    {
      var errorClass = exception.GetType().Name;
      var stackFrames = new System.Diagnostics.StackTrace(exception, true).GetFrames();

      StackTraceLine[] lines = null;

      if (stackFrames != null && stackFrames.Length > 0)
      {
        lines = new StackTrace(stackFrames).ToArray();
      }
      else
      {
        lines = new StackTrace(alternativeStackTrace).ToArray();
      }

      return new Exception(errorClass, exception.Message, lines);
    }

    public static Exception FromUnityLogMessage(UnityLogMessage logMessage, System.Diagnostics.StackFrame[] stackFrames, Severity severity)
    {
      return FromUnityLogMessage(logMessage, stackFrames, severity, false);
    }

    public static Exception FromUnityLogMessage(UnityLogMessage logMessage, System.Diagnostics.StackFrame[] fallbackStackFrames, Severity severity, bool forceUnhandled)
    {
      var match = Regex.Match(logMessage.Condition, ErrorClassMessagePattern, RegexOptions.Singleline);

      var lines = new StackTrace(logMessage.StackTrace).ToArray();
      if (lines.Length == 0) {
        lines = new StackTrace(fallbackStackFrames).ToArray();
      }

      var handledState = forceUnhandled
        ? HandledState.ForUnhandledException()
        : HandledState.ForUnityLogMessage(severity);

      if (match.Success)
      {
        var errorClass = match.Groups["errorClass"].Value;
        var message = match.Groups["message"].Value.Trim();
        // Exceptions starting with "AndroidJavaException" are uncaught Java exceptions reported
        // via the Unity log handler
        if (errorClass == AndroidJavaErrorClass)
        {
          match = Regex.Match(message, ErrorClassMessagePattern, RegexOptions.Singleline);

          // If the message matches the "class: message" pattern, then the Java class is followed
          // by a description of the Java exception. These two values will be used as the error
          // class and message.
          if (match.Success)
          {
            errorClass = match.Groups["errorClass"].Value;
            message = match.Groups["message"].Value.Trim();
          }
          else
          {
            // There was no Java exception description, so the Java class is the only content in
            // the message.
            errorClass = message;
            message = "";
          }
          lines = new StackTrace(logMessage.StackTrace, StackTraceFormat.AndroidJava).ToArray();
          handledState = HandledState.ForUnhandledException();
        }
        return new Exception(errorClass, message, lines, handledState);
      }
      else
      {
        // include the type somehow in there
        return new Exception($"UnityLog{logMessage.Type}", logMessage.Condition, lines, handledState);
      }
    }

    /// <summary>
    /// Validates the logMessage excluding previously delivered reports
    /// </summary>
    public static bool ShouldSend(UnityLogMessage logMessage)
    {
      var match = Regex.Match(logMessage.Condition, ErrorClassMessagePattern, RegexOptions.Singleline);
      if (match.Success)
      {
        var errorClass = match.Groups["errorClass"].Value;
        if (errorClass == AndroidJavaErrorClass)
        {
          return logMessage.StackTrace == null
            || !logMessage.StackTrace.Contains(BugsnagStackTraceMarker);
        }
      }
      return true;
    }
  }
}
