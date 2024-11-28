
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
                new ProjectTypeViewModel { Id = 0, TypeName = "New Project 1", IsActive = true },
                new ProjectTypeViewModel { Id = 0, TypeName = "New Project 2", IsActive = false }
            };

            var addResult = await app.AddRecordsWithRetryAsync<ProjectTypeViewModel>(
                newRecords,
                filter: vm => vm.IsActive == true // Save active records only
            );
            Console.WriteLine($"Added Records: {addResult.SavedRecords.Count}, Failed: {addResult.FailedRecords.Count}");

            // Example: Editing records
            var idsToUpdate = new List<int> { 1, 2 };
            var editResult = await app.EditRecordsWithRetryAsync<ProjectTypeViewModel>(
                filter: vm => idsToUpdate.Contains(vm.Id), // Update records with specified IDs
                updateAction: vm => vm.TypeName = "Updated Name" // Modify the name
            );
            Console.WriteLine($"Updated Records: {editResult.UpdatedEntities.Count}, Failed: {editResult.FailedRecords.Count}");

            // Example: Deleting records
            var deleteResult = await app.DeleteRecordsWithRetryAsync<ProjectTypeViewModel>(
                filter: vm => vm.IsActive == false // Delete inactive records
            );
            Console.WriteLine($"Deleted Records: {deleteResult.DeletedCount}, Failed: {deleteResult.FailedRecords.Count}");

            // Example: Fetching records
            var fetchResult = await app.GetRecordsWithRetryAsync<ProjectTypeViewModel>(
                filter: vm => vm.IsActive == true // Fetch active records
            );
            Console.WriteLine($"Fetched Records: {fetchResult.Records.Count}, Failed: {fetchResult.FailedRecords.Count}");
        }
    }
}
