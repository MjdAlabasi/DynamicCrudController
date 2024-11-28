
# Dynamic CRUD Controller

This project provides a dynamic, reusable CRUD controller for .NET applications using EF Core and AutoMapper.
It simplifies operations such as adding, editing, deleting, and fetching records.

## Features
- Dynamic CRUD operations for any entity and ViewModel.
- Supports filtering, retry logic, and error handling.
- Built with EF Core 8.0 and AutoMapper.

## Requirements
- .NET 6 or higher
- EF Core 8.0
- AutoMapper

## Installation
1. Clone the repository or download the source code.
2. Add the files to your project.
3. Install required NuGet packages:
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
```

## Contributing
Feel free to fork this repository and submit pull requests.

## License
This project is licensed under the MIT License. See the LICENSE file for more details.
