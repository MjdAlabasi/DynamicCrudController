
namespace MiniProjectDesigner.Helpers
{
    public static class ErrorManager
    {
        public static List<object> FailedRecords { get; } = new();

        public static void AddFailedRecord<T>(T record) where T : class
        {
            FailedRecords.Add(record);
        }

        public static void ClearFailedRecords()
        {
            FailedRecords.Clear();
        }
    }
}
