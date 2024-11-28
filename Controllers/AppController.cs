using System.Linq.Expressions;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using AutoMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MiniProjectDesigner.Models.Data;
using MiniProjectDesigner.Tools;
using MiniProjectDesigner.ViewModels;
using Convert = System.Convert;
using Task = System.Threading.Tasks.Task;

namespace MiniProjectDesigner.Controllers;

public class AppController
{
    private readonly IMapper _mapper = MapperConfig.InitializeAutoMapper();

    /// <summary>
    /// Adds a list of records to the database with retry logic in case of failures.
    /// </summary>
    /// <typeparam name="TViewModel">
    /// The type of the ViewModel representing the records to be added. Must implement <see cref="IViewModel"/>.
    /// </typeparam>
    /// <param name="viewModels">
    /// A list of ViewModel objects representing the records to be added to the database.
    /// </param>
    /// <param name="filter">
    /// An optional filter expression to selectively add records based on certain conditions.
    /// If no filter is provided, all records in the provided list will be processed.
    /// </param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>SavedRecords</c>: A list of successfully saved entities.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>FailedRecords</c>: A list of records that failed to save, along with error details.
    /// </description>
    /// </item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// - This method retries up to three times for any record that fails to save.
    /// - Records are saved individually, and errors are logged for each failure.
    /// - If a filter is provided, only records that match the filter will be processed.
    /// - The method dynamically resolves the entity type and primary key from the ViewModel.
    /// </remarks>
    public async Task<(List<object> SavedRecords, List<FailedRecord<TViewModel>> FailedRecords)>
        AddRecordsWithRetryAsync<TViewModel>(
            List<TViewModel> viewModels, // The list of ViewModel objects to add.
            Expression<Func<TViewModel, bool>> filter = null // Optional filter to select specific records.
        )
        where TViewModel : class, IViewModel, new() // Ensures TViewModel implements IViewModel and has a parameterless constructor.
    {
        // List to store successfully saved records.
        var savedRecords = new List<object>();

        // List to store records that failed to save, including error details.
        var failedRecords = new List<FailedRecord<TViewModel>>();

        // Maximum number of retry attempts for failed operations.
        var maxRetries = 3;

        // Apply the filter if provided, otherwise process all records.
        var retryRecords = filter != null
            ? viewModels.Where(filter.Compile()).ToList() // Filter records based on the provided filter.
            : new List<TViewModel>(viewModels); // Otherwise, use all records.

        // If there are no records to process, return empty results.
        if (retryRecords == null || !retryRecords.Any())
            return (savedRecords, failedRecords);

        // Loop for a maximum of maxRetries attempts.
        for (var attempt = 1; attempt <= maxRetries && retryRecords.Any(); attempt++)
        {
            // Iterate over a copy of retryRecords to avoid modifying the collection during iteration.
            foreach (var viewModel in retryRecords.ToList())
            {
                await using var context = new MiniContext(); // Create a new database context for this operation.

                try
                {
                    // Get the type of the entity associated with the ViewModel.
                    var entityType = new TViewModel().EntityType;
                    if (entityType == null)
                    {
                        // If the entity type is not defined, mark the record as failed.
                        var failed = ErrorHandler.CreateFailedRecord(viewModel,
                            new Exception("Entity type is not defined."));
                        failedRecords.Add(failed);
                        continue; // Skip to the next record.
                    }

                    // Get the DbSet for the entity type using a helper method.
                    var dbSet = EntityHelper.GetDbSet(context, entityType);

                    // Get the primary key property name for the ViewModel.
                    var primaryKeyName = EntityHelper.GetPrimaryKeyName<TViewModel>();

                    // Get the property info for the primary key.
                    var idProperty = typeof(TViewModel).GetProperty(primaryKeyName);

                    // If the primary key is an integer, set its value to 0 to avoid conflicts.
                    if (idProperty != null && idProperty.PropertyType == typeof(int))
                        idProperty.SetValue(viewModel, 0);

                    // Create a new instance of the entity from the ViewModel using AutoMapper.
                    var newEntity = EntityHelper.CreateEntityInstance(entityType, viewModel, _mapper);

                    // Add the new entity to the DbSet using reflection.
                    dbSet.GetType().GetMethod("Add")?.Invoke(dbSet, new[] { newEntity });

                    // Save changes to the database.
                    await context.SaveChangesAsync();

                    // If the record is saved successfully, add it to the savedRecords list.
                    savedRecords.Add(newEntity);

                    // Remove the successfully saved record from retryRecords.
                    retryRecords.Remove(viewModel);

                    // If the record was previously in the failedRecords list, remove it.
                    var failedRecord = failedRecords.FirstOrDefault(fr => fr.Record.Equals(viewModel));
                    if (failedRecord != null)
                    {
                        failedRecords.Remove(failedRecord);
                        ErrorManager.FailedRecords.Remove(failedRecord); // Also remove it from the shared error manager.
                    }
                }
                catch (Exception ex)
                {
                    // If an error occurs, add the record to the failedRecords list if not already there.
                    if (!failedRecords.Any(fr => fr.Record.Equals(viewModel)))
                    {
                        var failedRecord = ErrorHandler.CreateFailedRecord(viewModel, ex);
                        failedRecords.Add(failedRecord);
                    }
                }
            }

            // Exit the retry loop early if all records have been processed successfully.
            if (!retryRecords.Any())
                break;
        }

        // Return the list of saved records and failed records.
        return (savedRecords, failedRecords);
    }


    /// <summary>
    /// Updates a list of records in the database with retry logic for handling failures.
    /// </summary>
    /// <typeparam name="TViewModel">
    /// The type of the ViewModel that implements <see cref="IViewModel"/>.
    /// </typeparam>
    /// <param name="viewModels">
    /// A list of ViewModel objects to be updated. If null, the records are fetched from the database based on the filter or updateAll flag.
    /// </param>
    /// <param name="filter">
    /// An optional filter expression to determine which records should be updated if <paramref name="viewModels"/> is null.
    /// </param>
    /// <param name="updateAction">
    /// An optional action to modify each ViewModel before mapping it to the entity.
    /// </param>
    /// <param name="updateAll">
    /// A boolean flag to update all records in the database for the specified entity type.
    /// </param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>UpdatedEntities</c>: A list of successfully updated entities.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>FailedRecords</c>: A list of records that failed to update, including error details.
    /// </description>
    /// </item>
    /// </list>
    /// </returns>
    public async Task<(List<object> UpdatedEntities, List<FailedRecord<TViewModel>> FailedRecords)>
        EditRecordsWithRetryAsync<TViewModel>(
            List<TViewModel> viewModels = null, // List of records to update.
            Expression<Func<TViewModel, bool>> filter = null, // Filter to select records if viewModels is null.
            Action<TViewModel> updateAction = null, // Optional action to modify each record before updating.
            bool updateAll = false // If true, all records of the specified type will be updated.
        )
        where TViewModel : class, IViewModel, new() // Ensures TViewModel implements IViewModel and has a parameterless constructor.
    {
        // List to store successfully updated entities.
        var updatedEntities = new List<object>();

        // List to store records that failed to update, including error details.
        var failedRecords = new List<FailedRecord<TViewModel>>();

        // Maximum number of retry attempts for failed updates.
        var maxRetries = 3;

        // If no viewModels are provided, try fetching records from the database based on the filter or updateAll flag.
        if (viewModels == null || !viewModels.Any())
        {
            // If no filter is provided and updateAll is false, there's nothing to update.
            if (filter == null && !updateAll) return (updatedEntities, failedRecords);

            try
            {
                // Create a new database context for fetching records.
                await using var context = new MiniContext();

                // Get the entity type from the ViewModel.
                var entityType = new TViewModel().EntityType;
                if (entityType == null) throw new InvalidOperationException("Entity type is not defined.");

                // Get the DbSet for the specified entity type.
                var dbSet = EntityHelper.GetDbSet(context, entityType);

                // Apply the filter or fetch all records based on updateAll.
                var query = dbSet;
                if (!updateAll && filter != null)
                {
                    // Translate the filter expression from ViewModel to Entity.
                    var parameter = Expression.Parameter(entityType, "entity");
                    var body = new ReplaceExpressionVisitor(filter.Parameters[0], parameter).Visit(filter.Body);
                    var translatedFilter = Expression.Lambda(body, parameter);

                    // Apply the translated filter to the query.
                    query = EntityHelper.ApplyFilter(query, translatedFilter);
                }

                // Fetch the records and map them to ViewModels.
                var entities = await query.ToListAsync();
                viewModels = _mapper.Map<List<TViewModel>>(entities);
            }
            catch (Exception ex)
            {
                // Log and return the error if fetching records fails.
                failedRecords.Add(ErrorHandler.CreateFailedRecord<TViewModel>(null, ex));
                return (updatedEntities, failedRecords);
            }
        }

        // List to track records that need retry attempts.
        var retryRecords = new List<TViewModel>(viewModels);

        // Loop for retrying failed updates up to maxRetries times.
        for (var attempt = 1; attempt <= maxRetries && retryRecords.Any(); attempt++)
        {
            foreach (var viewModel in retryRecords.ToList())
            {
                await using var context = new MiniContext(); // Create a new database context for each record.

                try
                {
                    // Get the entity type from the ViewModel.
                    var entityType = new TViewModel().EntityType;
                    if (entityType == null)
                    {
                        // If the entity type is not defined, mark the record as failed.
                        var failed =
                            ErrorHandler.CreateFailedRecord(viewModel, new Exception("Entity type is not defined."));
                        failedRecords.Add(failed);
                        continue;
                    }

                    // Get the DbSet for the entity type.
                    var dbSet = EntityHelper.GetDbSet(context, entityType);

                    // Build the primary key filter to find the entity in the database.
                    var primaryKeyFilter = EntityHelper.BuildPrimaryKeyFilter(
                        entityType,
                        EntityHelper.GetPrimaryKeyName<TViewModel>(),
                        viewModel);

                    // Translate the filter for LINQ-to-Entities compatibility.
                    var originalParameter = primaryKeyFilter.Parameters[0];
                    var castedParameter = Expression.Parameter(typeof(object), originalParameter.Name);
                    var convertedBody =
                        Expression.Invoke(primaryKeyFilter, Expression.Convert(castedParameter, entityType));
                    var lambda = Expression.Lambda<Func<object, bool>>(convertedBody, castedParameter);

                    // Fetch the entity from the database.
                    var entity = await dbSet.FirstOrDefaultAsync(lambda);
                    if (entity == null)
                    {
                        // If the entity is not found, mark the record as failed.
                        var failed =
                            ErrorHandler.CreateFailedRecord(viewModel, new Exception("Entity not found for update."));
                        failedRecords.Add(failed);
                        continue;
                    }

                    // Apply the update action, if provided.
                    if (updateAction != null)
                    {
                        updateAction(viewModel); // Modify the ViewModel.
                        _mapper.Map(viewModel, entity); // Map changes to the entity.
                    }
                    else
                    {
                        // If no action is provided, map the ViewModel directly to the entity.
                        _mapper.Map(viewModel, entity);
                    }

                    // Save the updated entity to the database.
                    await context.SaveChangesAsync();

                    // If successful, add the entity to the updatedEntities list.
                    updatedEntities.Add(entity);

                    // Remove the record from retryRecords.
                    retryRecords.Remove(viewModel);

                    // If the record was in failedRecords, remove it.
                    var failedRecord = failedRecords.FirstOrDefault(fr => fr.Record.Equals(viewModel));
                    if (failedRecord != null)
                    {
                        failedRecords.Remove(failedRecord);
                        ErrorManager.FailedRecords.Remove(failedRecord); // Also remove it from the shared error manager.
                    }
                }
                catch (Exception ex)
                {
                    // If an error occurs, add the record to failedRecords if not already there.
                    if (!failedRecords.Any(fr => fr.Record.Equals(viewModel)))
                    {
                        var failedRecord = ErrorHandler.CreateFailedRecord(viewModel, ex);
                        failedRecords.Add(failedRecord);
                    }
                }
            }

            // Exit the retry loop early if all records have been processed successfully.
            if (!retryRecords.Any()) break;
        }

        // Return the list of updated entities and failed records.
        return (updatedEntities, failedRecords);
    }

    /// <summary>
    /// Deletes a list of records from the database with retry logic in case of failures.
    /// </summary>
    /// <typeparam name="TViewModel">
    /// The type of the ViewModel that implements <see cref="IViewModel"/>.
    /// </typeparam>
    /// <param name="viewModels">
    /// A list of ViewModel objects to delete. If null, the records are fetched from the database based on the filter or deleteAll flag.
    /// </param>
    /// <param name="filter">
    /// An optional filter expression to determine which records should be deleted if <paramref name="viewModels"/> is null.
    /// </param>
    /// <param name="deleteAll">
    /// A boolean flag to delete all records of the specified type.
    /// </param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>DeletedCount</c>: The number of records successfully deleted.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>FailedRecords</c>: A list of records that failed to delete, including error details.
    /// </description>
    /// </item>
    /// </list>
    /// </returns>
    public async Task<(int DeletedCount, List<FailedRecord<TViewModel>> FailedRecords)>
        DeleteRecordsWithRetryAsync<TViewModel>(
            List<TViewModel> viewModels = null, // List of records to delete.
            Expression<Func<TViewModel, bool>> filter = null, // Filter to select records if viewModels is null.
            bool deleteAll = false // If true, all records of the specified type will be deleted.
        )
        where TViewModel : class, IViewModel, new() // Ensures TViewModel implements IViewModel and has a parameterless constructor.
    {
        // List to store records that failed to delete, including error details.
        var failedRecords = new List<FailedRecord<TViewModel>>();

        // Counter for the number of successfully deleted records.
        var deletedCount = 0;

        // Maximum number of retry attempts for failed deletions.
        var maxRetries = 3;

        // If no viewModels are provided, fetch records from the database based on the filter or deleteAll flag.
        if (viewModels == null || !viewModels.Any())
        {
            // If no filter is provided and deleteAll is false, there's nothing to delete.
            if (filter == null && !deleteAll) return (deletedCount, failedRecords);

            try
            {
                // Create a new database context for fetching records.
                await using var context = new MiniContext();

                // Get the entity type from the ViewModel.
                var entityType = new TViewModel().EntityType;
                if (entityType == null) throw new InvalidOperationException("Entity type is not defined.");

                // Get the DbSet for the specified entity type.
                var dbSet = EntityHelper.GetDbSet(context, entityType);

                // Apply the filter or fetch all records based on deleteAll.
                var query = dbSet;
                if (!deleteAll && filter != null)
                {
                    // Translate the filter expression from ViewModel to Entity.
                    var parameter = Expression.Parameter(entityType, "entity");
                    var body = new ReplaceExpressionVisitor(filter.Parameters[0], parameter).Visit(filter.Body);
                    var translatedFilter = Expression.Lambda(body, parameter);

                    // Apply the translated filter to the query.
                    query = EntityHelper.ApplyFilter(query, translatedFilter);
                }

                // Fetch the records and map them to ViewModels.
                var entities = await query.ToListAsync();
                viewModels = _mapper.Map<List<TViewModel>>(entities);
            }
            catch (Exception ex)
            {
                // Log and return the error if fetching records fails.
                failedRecords.Add(ErrorHandler.CreateFailedRecord<TViewModel>(null, ex));
                return (deletedCount, failedRecords);
            }
        }

        // List to track records that need retry attempts.
        var retryRecords = new List<TViewModel>(viewModels);

        // Loop for retrying failed deletions up to maxRetries times.
        for (var attempt = 1; attempt <= maxRetries && retryRecords.Any(); attempt++)
        {
            foreach (var viewModel in retryRecords.ToList())
            {
                await using var context = new MiniContext(); // Create a new database context for each record.

                try
                {
                    // Get the entity type from the ViewModel.
                    var entityType = new TViewModel().EntityType;
                    if (entityType == null)
                    {
                        // If the entity type is not defined, mark the record as failed.
                        var failed =
                            ErrorHandler.CreateFailedRecord(viewModel, new Exception("Entity type is not defined."));
                        failedRecords.Add(failed);
                        continue;
                    }

                    // Get the DbSet for the entity type.
                    var dbSet = EntityHelper.GetDbSet(context, entityType);

                    // Build the primary key filter to find the entity in the database.
                    var primaryKeyFilter = EntityHelper.BuildPrimaryKeyFilter(
                        entityType,
                        EntityHelper.GetPrimaryKeyName<TViewModel>(),
                        viewModel);

                    // Translate the filter for LINQ-to-Entities compatibility.
                    var originalParameter = primaryKeyFilter.Parameters[0];
                    var castedParameter = Expression.Parameter(typeof(object), originalParameter.Name);
                    var convertedBody =
                        Expression.Invoke(primaryKeyFilter, Expression.Convert(castedParameter, entityType));
                    var lambda = Expression.Lambda<Func<object, bool>>(convertedBody, castedParameter);

                    // Fetch the entity from the database.
                    var entity = await dbSet.FirstOrDefaultAsync(lambda);

                    if (entity != null)
                    {
                        // Remove the entity from the database.
                        dbSet.GetType().GetMethod("Remove")?.Invoke(dbSet, new[] { entity });

                        // Save the changes to the database.
                        await context.SaveChangesAsync();

                        // Increment the deleted count and remove the record from retryRecords.
                        deletedCount++;
                        retryRecords.Remove(viewModel);

                        // If the record was in failedRecords, remove it.
                        var failedRecord = failedRecords.FirstOrDefault(fr => fr.Record.Equals(viewModel));
                        if (failedRecord != null)
                        {
                            failedRecords.Remove(failedRecord);
                            ErrorManager.FailedRecords.Remove(failedRecord); // Also remove it from the shared error manager.
                        }
                    }
                    else
                    {
                        // If the entity is not found, mark the record as failed.
                        var failed = ErrorHandler.CreateFailedRecord(viewModel,
                            new Exception("Entity not found for deletion."));
                        failedRecords.Add(failed);
                    }
                }
                catch (Exception ex)
                {
                    // If an error occurs, add the record to failedRecords if not already there.
                    if (!failedRecords.Any(fr => fr.Record.Equals(viewModel)))
                    {
                        var failedRecord = ErrorHandler.CreateFailedRecord(viewModel, ex);
                        failedRecords.Add(failedRecord);
                    }
                }
            }

            // Exit the retry loop early if all records have been processed successfully.
            if (!retryRecords.Any()) break;
        }

        // Return the number of successfully deleted records and the list of failed records.
        return (deletedCount, failedRecords);
    }

    /// <summary>
    /// Retrieves records from the database with retry logic in case of failures.
    /// </summary>
    /// <typeparam name="TViewModel">
    /// The type of the ViewModel that implements <see cref="IViewModel"/>.
    /// </typeparam>
    /// <param name="id">
    /// An optional ID to filter the record by primary key. If provided, only the record with this ID will be retrieved.
    /// </param>
    /// <param name="filter">
    /// An optional filter expression to select specific records.
    /// </param>
    /// <param name="returnAll">
    /// A boolean flag to retrieve all records of the specified type if set to true.
    /// </param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>Records</c>: A list of retrieved ViewModel objects.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>FailedRecords</c>: A list of records that failed to retrieve, including error details.
    /// </description>
    /// </item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// - Supports dynamic filtering by primary key or custom filter.
    /// - Retries the retrieval process up to 3 times in case of transient errors.
    /// </remarks>
    public async Task<(List<TViewModel> Records, List<FailedRecord<TViewModel>> FailedRecords)>
        GetRecordsWithRetryAsync<TViewModel>(
            int? id = null, // Optional ID for filtering by primary key.
            Expression<Func<TViewModel, bool>> filter = null, // Optional filter for retrieving specific records.
            bool returnAll = false // If true, all records will be retrieved.
        )
        where TViewModel : class, IViewModel, new() // Ensures TViewModel implements IViewModel and has a parameterless constructor.
    {
        // List to store records that failed to retrieve, including error details.
        var failedRecords = new List<FailedRecord<TViewModel>>();

        // List to store successfully retrieved ViewModel records.
        var records = new List<TViewModel>();

        // Maximum number of retry attempts for failed retrievals.
        const int maxRetries = 3;

        // Retry loop to handle transient errors.
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Create a new database context for this retrieval operation.
                await using var context = new MiniContext();

                // Get the entity type from the ViewModel.
                var entityType = new TViewModel().EntityType;
                if (entityType == null)
                {
                    // If the entity type is not defined, record the failure.
                    var failedRecord = new FailedRecord<TViewModel>(
                        null,
                        "Entity type is not defined.",
                        $"Ensure that {typeof(TViewModel).Name} has a valid EntityType."
                    );

                    failedRecords.Add(failedRecord);
                    ErrorManager.FailedRecords.Add(failedRecord); // Log the error.
                    return (records, failedRecords); // Exit with the failure.
                }

                // Get the DbSet for the specified entity type.
                var dbSet = EntityHelper.GetDbSet(context, entityType);

                // Build the query based on the parameters provided.
                var query = dbSet;

                if (!returnAll) // Only filter if returnAll is false.
                {
                    if (id.HasValue) // If an ID is provided, filter by primary key.
                    {
                        var primaryKeyName = EntityHelper.GetPrimaryKeyName<TViewModel>();
                        var idFilter = EntityHelper.BuildPrimaryKeyFilter(entityType, primaryKeyName, id.Value);

                        // Convert the filter for LINQ-to-Entities compatibility.
                        var originalParameter = idFilter.Parameters[0];
                        var castedParameter = Expression.Parameter(typeof(object), originalParameter.Name);
                        var convertedBody =
                            Expression.Invoke(idFilter, Expression.Convert(castedParameter, entityType));
                        var lambda = Expression.Lambda<Func<object, bool>>(convertedBody, castedParameter);

                        query = query.Where(lambda);
                    }
                    else if (filter != null) // If a custom filter is provided, apply it.
                    {
                        try
                        {
                            // Convert the ViewModel filter to an Entity-compatible filter.
                            var parameter = Expression.Parameter(entityType, "entity");
                            var body = new ReplaceExpressionVisitor(filter.Parameters[0], parameter)
                                .Visit(filter.Body);

                            var translatedFilter = Expression.Lambda(body, parameter);
                            query = EntityHelper.ApplyFilter(query, translatedFilter);
                        }
                        catch (Exception ex)
                        {
                            // Log the failure if the filter translation fails.
                            var failedRecord = new FailedRecord<TViewModel>(
                                null,
                                $"Failed to apply filter: {ex.Message}",
                                ex.InnerException?.Message
                            );

                            failedRecords.Add(failedRecord);
                            ErrorManager.FailedRecords.Add(failedRecord); // Log the error.
                            return (records, failedRecords); // Exit with the failure.
                        }
                    }
                    else
                    {
                        // If no criteria are provided, return a failure.
                        var failedRecord = new FailedRecord<TViewModel>(
                            null,
                            "No criteria provided for retrieval.",
                            "Provide either an ID, a filter, or set returnAll to true."
                        );

                        failedRecords.Add(failedRecord);
                        ErrorManager.FailedRecords.Add(failedRecord); // Log the error.
                        return (records, failedRecords); // Exit with the failure.
                    }
                }

                // Execute the query and fetch the records from the database.
                var entities = await query.ToListAsync();

                // Map the entities to ViewModels using AutoMapper.
                records = _mapper.Map<List<TViewModel>>(entities);

                // If successful, clear any previous failures from the error manager.
                foreach (var failedRecord in failedRecords)
                    ErrorManager.FailedRecords.Remove(failedRecord);

                return (records, failedRecords); // Return the successfully retrieved records.
            }
            catch (Exception ex)
            {
                // Log the failure if an exception occurs.
                var failedRecord = ErrorHandler.CreateFailedRecord<TViewModel>(null, ex);
                failedRecords.Add(failedRecord);

                if (!ErrorManager.FailedRecords.Contains(failedRecord))
                    ErrorManager.FailedRecords.Add(failedRecord); // Log the error.

                if (attempt == maxRetries) return (records, failedRecords); // Exit after max retries.

                // Delay before retrying to handle transient errors.
                await Task.Delay(1000);
            }
        }

        // Return the results after all retries.
        return (records, failedRecords);
    }
}

public static class EntityHelper
{
    public static IQueryable<object> GetDbSet(DbContext context, Type entityType)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType), "Entity type cannot be null.");

        var method = typeof(DbContext)
            .GetMethods()
            .FirstOrDefault(m => m.Name == nameof(context.Set)
                                 && m.IsGenericMethod
                                 && m.GetParameters().Length == 0);
        if (method != null)
        {
            var genericMethod = method.MakeGenericMethod(entityType);
            return genericMethod.Invoke(context, null) as IQueryable<object>
                   ?? throw new InvalidOperationException($"DbSet for '{entityType.Name}' could not be retrieved.");
        }

        throw new InvalidOperationException("Could not find the 'Set' method on DbContext.");
    }

    public static IQueryable<object> ApplyFilter(IQueryable<object> query, LambdaExpression filter)
    {
        if (filter == null) return query;

        var whereMethod = typeof(Queryable).GetMethods()
            .First(m => m.Name == "Where" && m.GetParameters().Length == 2)
            .MakeGenericMethod(query.ElementType);

        return whereMethod.Invoke(null, [query, filter]) as IQueryable<object>
               ?? throw new InvalidOperationException("Failed to apply filter to query.");
    }

    public static LambdaExpression BuildPrimaryKeyFilter(Type entityType, string primaryKeyName,
        object primaryKeyValue)
    {
        // إذا كانت القيمة كائن ViewModel، قم باستخراج قيمة المفتاح الأساسي
        if (primaryKeyValue != null && primaryKeyValue.GetType().GetProperty(primaryKeyName) != null)
            primaryKeyValue = primaryKeyValue.GetType().GetProperty(primaryKeyName)?.GetValue(primaryKeyValue)
                              ?? throw new InvalidOperationException(
                                  "Failed to extract primary key value from ViewModel.");

        var parameter = Expression.Parameter(entityType, "entity");
        var propertyAccess = Expression.Property(parameter, primaryKeyName);

        // التأكد من مطابقة النوع وتحويل القيمة إذا لزم الأمر
        var propertyType = propertyAccess.Type;
        var convertedValue = primaryKeyValue is IConvertible
            ? Convert.ChangeType(primaryKeyValue, propertyType)
            : primaryKeyValue;

        var constantValue = Expression.Constant(convertedValue, propertyType);

        var equality = Expression.Equal(propertyAccess, constantValue);

        return Expression.Lambda(equality, parameter);
    }

    public static object CreateEntityInstance(Type entityType, object viewModel, IMapper mapper)
    {
        var entityInstance = Activator.CreateInstance(entityType);
        if (entityInstance != null)
        {
            mapper.Map(viewModel, entityInstance);
            return entityInstance;
        }

        throw new InvalidOperationException($"Failed to create an instance of '{entityType.Name}'.");
    }

    public static string GetPrimaryKeyName<TViewModel>() where TViewModel : class
    {
        var primaryKeyProperty = typeof(TViewModel).GetProperties()
            .FirstOrDefault(p => Attribute.IsDefined(p, typeof(ViewPrimaryKeyAttribute)));

        return primaryKeyProperty?.Name
               ?? throw new InvalidOperationException("Primary key property not found in ViewModel.");
    }
}

public class ReplaceExpressionVisitor : ExpressionVisitor
{
    private readonly Expression _newValue;
    private readonly Expression _oldValue;

    public ReplaceExpressionVisitor(Expression oldValue, Expression newValue)
    {
        _oldValue = oldValue;
        _newValue = newValue;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        // إذا كان المعامل الحالي هو المعامل القديم، استبدله بالجديد
        return node == _oldValue ? _newValue : base.VisitParameter(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // استبدال الخصائص بين الأنواع
        if (node.Expression == _oldValue)
        {
            var newMember = _newValue.Type.GetProperty(node.Member.Name);
            if (newMember == null)
                throw new ArgumentException(
                    $"Property '{node.Member.Name}' is not defined for type '{_newValue.Type}'.", nameof(node.Member));

            return Expression.MakeMemberAccess(_newValue, newMember);
        }

        return base.VisitMember(node);
    }
}

public class FailedRecord<TViewModel>
    where TViewModel : class
{
    public FailedRecord(
        TViewModel record,
        string errorMessage,
        string details = null,
        string exceptionType = null,
        string filePath = null,
        string callerName = null,
        int lineNumber = 0)
    {
        Record = record;
        ErrorMessage = errorMessage;
        Details = details;
        ExceptionType = exceptionType;
        FilePath = filePath;
        CallerName = callerName;
        LineNumber = lineNumber;
    }

    public TViewModel Record { get; set; }
    public string ErrorMessage { get; set; }
    public string Details { get; set; }
    public string ExceptionType { get; set; } // نوع الاستثناء
    public string FilePath { get; set; } // ملف المصدر
    public string CallerName { get; set; } // اسم الميثود
    public int LineNumber { get; set; } // رقم السطر
    public DateTime Timestamp { get; set; } = DateTime.Now; // وقت تسجيل الخطأ
}

public static class ErrorHandler
{
    public static string HandleException(Exception ex)
    {
        if (ex == null) return "An unknown error occurred.";

        // التعامل مع استثناءات قاعدة البيانات
        if (ex is DbUpdateException dbEx && dbEx.InnerException is SqlException sqlEx)
            return sqlEx.Number switch
            {
                2627 or 2601 => "Duplicate key violation (UNIQUE constraint).",
                547 => "Foreign key or check constraint violation.",
                1205 => "Deadlock occurred.",
                _ => $"SQL Error: {sqlEx.Message}"
            };
        if (ex is DbUpdateConcurrencyException)
            return "Concurrency conflict occurred while updating the database.";

        // التعامل مع استثناءات عامة
        if (ex is ArgumentNullException argEx)
            return $"Null argument provided: {argEx.ParamName}.";
        if (ex is ArgumentException argEx1)
            return $"Invalid argument: {argEx1.Message}.";
        if (ex is InvalidOperationException)
            return "An invalid operation was attempted.";
        if (ex is FormatException) return "Invalid data format provided.";

        // التعامل مع استثناءات النظام
        if (ex is OutOfMemoryException)
            return "The application ran out of memory.";
        if (ex is IOException ioEx) return $"I/O error occurred: {ioEx.Message}.";

        // التعامل مع استثناءات الشبكة
        if (ex is HttpRequestException httpEx)
            return $"HTTP request error: {httpEx.Message}.";
        if (ex is SocketException socketEx)
            return $"Socket error: {socketEx.Message} (Code: {socketEx.SocketErrorCode}).";

        // التعامل مع استثناءات المهام
        if (ex is TaskCanceledException)
            return "The operation was canceled. Please try again.";
        if (ex is TimeoutException) return "The operation timed out. Please try again later.";

        // استثناءات غير متوقعة
        return $"An unhandled exception occurred: {ex.GetType().Name} - {ex.Message}";
    }

    public static FailedRecord<TViewModel> CreateFailedRecord<TViewModel>(
        TViewModel record,
        Exception ex,
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string callerName = "",
        [CallerLineNumber] int lineNumber = 0)
        where TViewModel : class
    {
        // تفاصيل الاستثناء الداخلي إذا كانت موجودة
        var innerExceptionDetails = ex.InnerException != null
            ? $"Inner Exception: {ex.InnerException.Message}. StackTrace: {ex.InnerException.StackTrace}"
            : ex.StackTrace;

        // تحديد نوع الاستثناء الأساسي ونوع الاستثناء الداخلي إن وجد
        var exceptionType = ex.GetType().Name;
        var innerExceptionType = ex.InnerException?.GetType().Name;

        var detailedExceptionType = innerExceptionType != null
            ? $"{exceptionType} (Inner: {innerExceptionType})"
            : exceptionType;

        // إضافة تفاصيل إضافية للأخطاء الشائعة في EF أو الأخطاء الأخرى
        var additionalErrorDetails = string.Empty;

        if (ex is DbUpdateException dbEx)
            additionalErrorDetails = $"DB Update Error: {dbEx.Message}.";
        else if (ex is SqlException sqlEx)
            additionalErrorDetails = $"SQL Error Code: {sqlEx.Number}. {sqlEx.Message}";
        else if (ex is InvalidOperationException invalidOpEx)
            additionalErrorDetails = $"Invalid Operation Error: {invalidOpEx.Message}";
        else if (ex is TimeoutException timeoutEx) additionalErrorDetails = $"Timeout Error: {timeoutEx.Message}.";

        // دمج جميع التفاصيل مع المعلومات الأخرى
        var detailedMessage =
            $"Error occurred - Details: {innerExceptionDetails}. {additionalErrorDetails}";

        var failedRecord = new FailedRecord<TViewModel>(
            record,
            ex.Message,
            detailedMessage,
            detailedExceptionType,
            filePath,
            callerName,
            lineNumber
        );

        // إضافة السجل إلى القائمة المشتركة
        ErrorManager.AddFailedRecord(failedRecord);

        return failedRecord;
    }
}

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