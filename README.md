
# Dynamic CRUD Controller

A reusable, dynamic CRUD controller designed for .NET applications using EF Core 8.0 and AutoMapper. This library simplifies common database operations like creating, reading, updating, and deleting (CRUD) records, with support for retry logic, filtering, and error handling.

## Features

- **Dynamic CRUD Operations**: Works seamlessly with any entity and ViewModel structure.
- **Retry Logic**: Handles transient errors with retry mechanisms for database operations.
- **Filter Support**: Apply custom filters for selective operations.
- **Error Management**: Built-in error handling with detailed logging for failed operations.
- **Flexible Integration**: Designed to work with EF Core 8.0 and AutoMapper for streamlined data mapping.

## Requirements

- **.NET Framework**: .NET 6.0 or higher.
- **Libraries**:
  - Entity Framework Core 8.0
  - AutoMapper

## Installation

Clone this repository to your local machine:
```bash
git clone https://github.com/MjdAlabasi/DynamicCrudController.git
cd DynamicCrudController
```

Install the required NuGet packages:
```bash
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package AutoMapper
```

## Usage

### Example: Adding Records

```csharp
var newRecords = new List<ProjectTypeViewModel>
{
    new ProjectTypeViewModel { Id = 0, TypeName = "New Project 1", IsActive = true },
    new ProjectTypeViewModel { Id = 0, TypeName = "New Project 2", IsActive = false }
};

var result = await app.AddRecordsWithRetryAsync<ProjectTypeViewModel>(
    newRecords,
    filter: vm => vm.IsActive == true // Save active records only
);

Console.WriteLine($"Saved: {result.SavedRecords.Count}, Failed: {result.FailedRecords.Count}");
```

### Example: Editing Records

```csharp
var idsToUpdate = new List<int> { 1, 2 };
var editResult = await app.EditRecordsWithRetryAsync<ProjectTypeViewModel>(
    filter: vm => idsToUpdate.Contains(vm.Id), // Filter records by ID
    updateAction: vm => vm.TypeName = "Updated Name" // Update field
);

Console.WriteLine($"Updated: {editResult.UpdatedEntities.Count}, Failed: {editResult.FailedRecords.Count}");
```

### Example: Deleting Records

```csharp
var deleteResult = await app.DeleteRecordsWithRetryAsync<ProjectTypeViewModel>(
    filter: vm => vm.IsActive == false // Delete inactive records
);

Console.WriteLine($"Deleted: {deleteResult.DeletedCount}, Failed: {deleteResult.FailedRecords.Count}");
```

### Example: Fetching Records

```csharp
var fetchResult = await app.GetRecordsWithRetryAsync<ProjectTypeViewModel>(
    filter: vm => vm.IsActive == true // Fetch active records
);

Console.WriteLine($"Fetched: {fetchResult.Records.Count}, Failed: {fetchResult.FailedRecords.Count}");
```

## Project Structure

```
DynamicCrudController/
├── Controllers/
│   └── AppController.cs  # Main controller for CRUD operations
├── Helpers/
│   ├── EntityHelper.cs    # Utility functions for working with entities
│   ├── ErrorHandler.cs    # Centralized error handling
│   └── ErrorManager.cs    # Manages failed records and logging
├── Tools/
│   ├── AutoMapperProfile.cs  # AutoMapper configuration
│   └── MapperConfig.cs       # AutoMapper initialization
├── ViewModels/
│   ├── IViewModel.cs         # Interface and attributes for ViewModels
│   └── ProjectTypeViewModel.cs # Example ViewModel
├── Models/
│   ├── MiniContext.cs        # EF Core DbContext
│   └── AutomationRule.cs     # Example entity model
├── Docs/
│   ├── README.md             # Project documentation
│   └── LICENSE               # License information
└── Examples/
    └── ExampleUsage.cs       # Usage examples for CRUD operations
```

## Contributing

Contributions are welcome! Follow these steps to contribute:

1. Fork the repository.
2. Create a new branch for your feature or bugfix:
   ```bash
   git checkout -b feature-name
   ```
3. Commit your changes:
   ```bash
   git commit -m "Description of changes"
   ```
4. Push your branch and create a pull request:
   ```bash
   git push origin feature-name
   ```

## License

This project is licensed under the [MIT License](LICENSE).

## Contact

For any questions or issues, please feel free to contact me through my GitHub profile or open an issue in the repository.
