
using MiniProjectDesigner.Controllers;

namespace MiniProjectDesigner.Helpers
{
    public static class ErrorManager
    {
        public static List<object> FailedRecords { get; } = new();

        public static void AddFailedRecord<TViewModel>(FailedRecord<TViewModel> record) where TViewModel : class
        {
            FailedRecords.Add(record);
        }

        public static void ClearFailedRecords()
        {
            FailedRecords.Clear();
        }
    }
}
