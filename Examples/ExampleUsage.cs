
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniProjectDesigner.Controllers;
using MiniProjectDesigner.ViewModels;

namespace MiniProjectDesigner.Examples
{
    public class ExampleUsage
    {
        public static async Task RunExamples(AppController app)
        {
            // Example: Adding records
            var newRecords = new List<ProjectTypeViewModel>
            {
                new ProjectTypeViewModel { Id = 0, TypeNameEn = "New Project 1",TypeNameAr = "ãÔÑæÚ ÌÏíÏ 1", IsActive = true, CreatedDate = DateTime.Now},
                new ProjectTypeViewModel { Id = 0, TypeNameEn = "New Project 2",TypeNameAr = "ãÔÑæÚ ÌÏíÏ 2", IsActive = true, CreatedDate = DateTime.Now},
            };

            var addResult = await app.AddRecordsWithRetryAsync<ProjectTypeViewModel>(
                newRecords,
                filter: vm => vm.IsActive == true // Save active records only
            );
            Console.WriteLine($@"Added Records: {addResult.SavedRecords.Count}, Failed: {addResult.FailedRecords.Count}");

            // Example: Editing records
            var editResult = await app.EditRecordsWithRetryAsync<ProjectTypeViewModel>(
                filter: vm => vm.IsActive == true, // Update records with specified IDs
                updateAction: vm => vm.IsActive = false// Modify the name
            );
            Console.WriteLine($@"Updated Records: {editResult.UpdatedEntities.Count}, Failed: {editResult.FailedRecords.Count}");

            // Example: Deleting records
            var deleteResult = await app.DeleteRecordsWithRetryAsync<ProjectTypeViewModel>(
                filter: vm => vm.IsActive == true // Delete inactive records
            );
            Console.WriteLine($@"Deleted Records: {deleteResult.DeletedCount}, Failed: {deleteResult.FailedRecords.Count}");

            // Example: Fetching records
            var fetchResult = await app.GetRecordsWithRetryAsync<ProjectTypeViewModel>(
                filter: vm => vm.IsActive == true // Fetch active records
            );
            Console.WriteLine($@"Fetched Records: {fetchResult.Records.Count}, Failed: {fetchResult.FailedRecords.Count}");
        }
    }
}
