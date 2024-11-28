
using System.Runtime.CompilerServices;

namespace MiniProjectDesigner.Helpers
{
    public static class ErrorHandler
    {
        public static string HandleException(Exception ex)
        {
            if (ex == null) return "An unknown error occurred.";
            if (ex is ArgumentNullException argEx) return $"Null argument: {argEx.ParamName}";
            if (ex is InvalidOperationException) return "Invalid operation attempted.";
            return $"Unhandled exception: {ex.Message}";
        }
    }
}
