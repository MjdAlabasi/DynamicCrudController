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

    // دالة الإضافة مع إعادة المحاولة
    public async Task<(List<object> SavedRecords, List<FailedRecord<TViewModel>> FailedRecords)>
        AddRecordsWithRetryAsync<TViewModel>(
            List<TViewModel> viewModels, Expression<Func<TViewModel, bool>> filter = null)
        where TViewModel : class, IViewModel, new()
    {
        var savedRecords = new List<object>();
        var failedRecords = new List<FailedRecord<TViewModel>>();
        var maxRetries = 3;
        // تصفية السجلات باستخدام الفلتر إذا كان موجودًا
        var retryRecords = filter != null
            ? viewModels.Where(filter.Compile()).ToList()
            : new List<TViewModel>(viewModels);

        if (retryRecords == null || !retryRecords.Any()) return (savedRecords, failedRecords);

        for (var attempt = 1; attempt <= maxRetries && retryRecords.Any(); attempt++)
        {
            foreach (var viewModel in retryRecords.ToList())
            {
                await using var context = new MiniContext();
                try
                {
                    var entityType = new TViewModel().EntityType;
                    if (entityType == null)
                    {
                        var failed = ErrorHandler.CreateFailedRecord(viewModel,
                            new Exception("Entity type is not defined."));
                        failedRecords.Add(failed);
                        continue; // انتقل إلى العنصر التالي
                    }

                    var dbSet = EntityHelper.GetDbSet(context, entityType);
                    var primaryKeyName = EntityHelper.GetPrimaryKeyName<TViewModel>();
                    var idProperty = typeof(TViewModel).GetProperty(primaryKeyName);

                    if (idProperty != null && idProperty.PropertyType == typeof(int))
                        idProperty.SetValue(viewModel, 0); // تعيين 0 لتجنب تعارض المفتاح

                    var newEntity = EntityHelper.CreateEntityInstance(entityType, viewModel, _mapper);
                    dbSet.GetType().GetMethod("Add")?.Invoke(dbSet, new[] { newEntity });

                    await context.SaveChangesAsync();

                    // إذا تم الحفظ بنجاح، أضف السجل إلى savedRecords
                    savedRecords.Add(newEntity);
                    retryRecords.Remove(viewModel);
                    var failedRecord = failedRecords.FirstOrDefault(fr => fr.Record.Equals(viewModel));
                    if (failedRecord != null)
                    {
                        failedRecords.Remove(failedRecord);
                        ErrorManager.FailedRecords.Remove(failedRecord);
                    }
                }
                catch (Exception ex)
                {
                    if (!failedRecords.Any(fr => fr.Record.Equals(viewModel)))
                    {
                        var failedRecord = ErrorHandler.CreateFailedRecord(viewModel, ex);
                        failedRecords.Add(failedRecord);
                    }
                }
            }

            if (!retryRecords.Any()) break;
        }

        return (savedRecords, failedRecords);
    }


    // دالة التعديل مع إعادة المحاولة
    public async Task<(List<object> UpdatedEntities, List<FailedRecord<TViewModel>> FailedRecords)>
        EditRecordsWithRetryAsync<TViewModel>(
            List<TViewModel> viewModels = null,
            Expression<Func<TViewModel, bool>> filter = null,
            Action<TViewModel> updateAction = null,
            bool updateAll = false)
        where TViewModel : class, IViewModel, new()
    {
        var updatedEntities = new List<object>();
        var failedRecords = new List<FailedRecord<TViewModel>>();
        var maxRetries = 3;

        // إذا لم يتم توفير قائمة viewModels
        if (viewModels == null || !viewModels.Any())
        {
            if (filter == null && !updateAll) return (updatedEntities, failedRecords); // لا يوجد ما يتم تحديثه

            // جلب السجلات من قاعدة البيانات بناءً على الفلتر أو updateAll
            try
            {
                await using var context = new MiniContext();
                var entityType = new TViewModel().EntityType;

                if (entityType == null) throw new InvalidOperationException("Entity type is not defined.");

                var dbSet = EntityHelper.GetDbSet(context, entityType);

                // تطبيق الفلتر أو تحديد كل السجلات
                var query = dbSet;
                if (!updateAll && filter != null)
                {
                    var parameter = Expression.Parameter(entityType, "entity");
                    var body = new ReplaceExpressionVisitor(filter.Parameters[0], parameter).Visit(filter.Body);
                    var translatedFilter = Expression.Lambda(body, parameter);
                    query = EntityHelper.ApplyFilter(query, translatedFilter);
                }

                var entities = await query.ToListAsync();
                viewModels = _mapper.Map<List<TViewModel>>(entities);
            }
            catch (Exception ex)
            {
                failedRecords.Add(ErrorHandler.CreateFailedRecord<TViewModel>(null, ex));
                return (updatedEntities, failedRecords);
            }
        }

        // العمل مع السجلات التي تم تمريرها أو جلبها
        var retryRecords = new List<TViewModel>(viewModels);

        for (var attempt = 1; attempt <= maxRetries && retryRecords.Any(); attempt++)
        {
            foreach (var viewModel in retryRecords.ToList())
            {
                await using var context = new MiniContext();

                try
                {
                    var entityType = new TViewModel().EntityType;
                    if (entityType == null)
                    {
                        var failed =
                            ErrorHandler.CreateFailedRecord(viewModel, new Exception("Entity type is not defined."));
                        failedRecords.Add(failed);
                        continue;
                    }

                    var dbSet = EntityHelper.GetDbSet(context, entityType);
                    var primaryKeyFilter = EntityHelper.BuildPrimaryKeyFilter(
                        entityType,
                        EntityHelper.GetPrimaryKeyName<TViewModel>(),
                        viewModel);

                    var originalParameter = primaryKeyFilter.Parameters[0];
                    var castedParameter = Expression.Parameter(typeof(object), originalParameter.Name);
                    var convertedBody =
                        Expression.Invoke(primaryKeyFilter, Expression.Convert(castedParameter, entityType));
                    var lambda = Expression.Lambda<Func<object, bool>>(convertedBody, castedParameter);

                    if (lambda == null)
                        throw new InvalidOperationException(
                            "Failed to convert primary key filter to the required type.");

                    // البحث عن الكيان في قاعدة البيانات
                    var entity = await dbSet.FirstOrDefaultAsync(lambda);
                    if (entity == null)
                    {
                        var failed =
                            ErrorHandler.CreateFailedRecord(viewModel, new Exception("Entity not found for update."));
                        failedRecords.Add(failed);
                        continue;
                    }

                    // تطبيق التعديلات
                    if (updateAction != null)
                    {
                        updateAction(viewModel);
                        _mapper.Map(viewModel, entity);
                    }
                    else
                    {
                        _mapper.Map(viewModel, entity);
                    }

                    // حفظ التغييرات
                    await context.SaveChangesAsync();

                    // إذا تم الحفظ بنجاح
                    updatedEntities.Add(entity);
                    retryRecords.Remove(viewModel);

                    // إزالة السجل من السجلات الفاشلة إذا تم إصلاحه
                    var failedRecord = failedRecords.FirstOrDefault(fr => fr.Record.Equals(viewModel));
                    if (failedRecord != null)
                    {
                        failedRecords.Remove(failedRecord);
                        ErrorManager.FailedRecords.Remove(failedRecord);
                    }
                }
                catch (Exception ex)
                {
                    if (!failedRecords.Any(fr => fr.Record.Equals(viewModel)))
                    {
                        var failedRecord = ErrorHandler.CreateFailedRecord(viewModel, ex);
                        failedRecords.Add(failedRecord);
                    }
                }
            }

            if (!retryRecords.Any()) break;
        }

        return (updatedEntities, failedRecords);
    }

    public async Task<(int DeletedCount, List<FailedRecord<TViewModel>> FailedRecords)>
        DeleteRecordsWithRetryAsync<TViewModel>(
            List<TViewModel> viewModels = null,
            Expression<Func<TViewModel, bool>> filter = null,
            bool deleteAll = false)
        where TViewModel : class, IViewModel, new()
    {
        var failedRecords = new List<FailedRecord<TViewModel>>();
        var deletedCount = 0;
        var maxRetries = 3;

        // إذا لم يتم توفير قائمة viewModels
        if (viewModels == null || !viewModels.Any())
        {
            if (filter == null && !deleteAll) return (deletedCount, failedRecords); // لا يوجد ما يتم حذفه

            // جلب السجلات من قاعدة البيانات بناءً على الفلتر أو deleteAll
            try
            {
                await using var context = new MiniContext();
                var entityType = new TViewModel().EntityType;

                if (entityType == null) throw new InvalidOperationException("Entity type is not defined.");

                var dbSet = EntityHelper.GetDbSet(context, entityType);

                // تطبيق الفلتر أو حذف كل السجلات
                var query = dbSet;
                if (!deleteAll && filter != null)
                {
                    var parameter = Expression.Parameter(entityType, "entity");
                    var body = new ReplaceExpressionVisitor(filter.Parameters[0], parameter).Visit(filter.Body);
                    var translatedFilter = Expression.Lambda(body, parameter);
                    query = EntityHelper.ApplyFilter(query, translatedFilter);
                }

                var entities = await query.ToListAsync();
                viewModels = _mapper.Map<List<TViewModel>>(entities);
            }
            catch (Exception ex)
            {
                failedRecords.Add(ErrorHandler.CreateFailedRecord<TViewModel>(null, ex));
                return (deletedCount, failedRecords);
            }
        }

        var retryRecords = new List<TViewModel>(viewModels);

        for (var attempt = 1; attempt <= maxRetries && retryRecords.Any(); attempt++)
        {
            foreach (var viewModel in retryRecords.ToList())
            {
                await using var context = new MiniContext();

                try
                {
                    var entityType = new TViewModel().EntityType;
                    if (entityType == null)
                    {
                        var failed =
                            ErrorHandler.CreateFailedRecord(viewModel, new Exception("Entity type is not defined."));
                        failedRecords.Add(failed);
                        continue;
                    }

                    var dbSet = EntityHelper.GetDbSet(context, entityType);

                    // إنشاء الفلتر الأساسي باستخدام المفتاح الرئيسي
                    var primaryKeyFilter = EntityHelper.BuildPrimaryKeyFilter(
                        entityType,
                        EntityHelper.GetPrimaryKeyName<TViewModel>(),
                        viewModel);

                    var originalParameter = primaryKeyFilter.Parameters[0];
                    var castedParameter = Expression.Parameter(typeof(object), originalParameter.Name);
                    var convertedBody =
                        Expression.Invoke(primaryKeyFilter, Expression.Convert(castedParameter, entityType));
                    var lambda = Expression.Lambda<Func<object, bool>>(convertedBody, castedParameter);

                    // البحث عن الكيان في قاعدة البيانات
                    var entity = await dbSet.FirstOrDefaultAsync(lambda);

                    if (entity != null)
                    {
                        dbSet.GetType().GetMethod("Remove")?.Invoke(dbSet, new[] { entity });
                        await context.SaveChangesAsync();
                        deletedCount++;
                        retryRecords.Remove(viewModel);

                        // إزالة السجل من السجلات الفاشلة إذا تم إصلاحه
                        var failedRecord = failedRecords.FirstOrDefault(fr => fr.Record.Equals(viewModel));
                        if (failedRecord != null)
                        {
                            failedRecords.Remove(failedRecord);
                            ErrorManager.FailedRecords.Remove(failedRecord);
                        }
                    }
                    else
                    {
                        var failed = ErrorHandler.CreateFailedRecord(viewModel,
                            new Exception("Entity not found for deletion."));
                        failedRecords.Add(failed);
                    }
                }
                catch (Exception ex)
                {
                    if (!failedRecords.Any(fr => fr.Record.Equals(viewModel)))
                    {
                        var failedRecord = ErrorHandler.CreateFailedRecord(viewModel, ex);
                        failedRecords.Add(failedRecord);
                    }
                }
            }

            if (!retryRecords.Any()) break;
        }

        return (deletedCount, failedRecords);
    }

    // دالة الجلب
    public async Task<(List<TViewModel> Records, List<FailedRecord<TViewModel>> FailedRecords)>
        GetRecordsWithRetryAsync<TViewModel>(
            int? id = null,
            Expression<Func<TViewModel, bool>> filter = null,
            bool returnAll = false)
        where TViewModel : class, IViewModel, new()
    {
        var failedRecords = new List<FailedRecord<TViewModel>>();
        var records = new List<TViewModel>();
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
            try
            {
                await using var context = new MiniContext();
                var entityType = new TViewModel().EntityType;

                if (entityType == null)
                {
                    var failedRecord = new FailedRecord<TViewModel>(
                        null,
                        "Entity type is not defined.",
                        $"Ensure that {typeof(TViewModel).Name} has a valid EntityType."
                    );

                    failedRecords.Add(failedRecord);
                    ErrorManager.FailedRecords.Add(failedRecord); // تسجيل الخطأ
                    return (records, failedRecords);
                }

                var dbSet = EntityHelper.GetDbSet(context, entityType);
                var query = dbSet;

                if (!returnAll)
                {
                    if (id.HasValue)
                    {
                        var primaryKeyName = EntityHelper.GetPrimaryKeyName<TViewModel>();
                        var idFilter = EntityHelper.BuildPrimaryKeyFilter(entityType, primaryKeyName, id.Value);

                        var originalParameter = idFilter.Parameters[0];
                        var castedParameter = Expression.Parameter(typeof(object), originalParameter.Name);
                        var convertedBody =
                            Expression.Invoke(idFilter, Expression.Convert(castedParameter, entityType));
                        var lambda = Expression.Lambda<Func<object, bool>>(convertedBody, castedParameter);

                        query = query.Where(lambda);
                    }
                    else if (filter != null)
                    {
                        try
                        {
                            var parameter = Expression.Parameter(entityType, "entity");
                            var body = new ReplaceExpressionVisitor(filter.Parameters[0], parameter)
                                .Visit(filter.Body);

                            var translatedFilter = Expression.Lambda(body, parameter);
                            query = EntityHelper.ApplyFilter(query, translatedFilter);
                        }
                        catch (Exception ex)
                        {
                            var failedRecord = new FailedRecord<TViewModel>(
                                null,
                                $"Failed to apply filter: {ex.Message}",
                                ex.InnerException?.Message
                            );

                            failedRecords.Add(failedRecord);
                            ErrorManager.FailedRecords.Add(failedRecord); // تسجيل الخطأ
                            return (records, failedRecords);
                        }
                    }
                    else
                    {
                        var failedRecord = new FailedRecord<TViewModel>(
                            null,
                            "No criteria provided for retrieval.",
                            "Provide either an ID, a filter, or set returnAll to true."
                        );

                        failedRecords.Add(failedRecord);
                        ErrorManager.FailedRecords.Add(failedRecord); // تسجيل الخطأ
                        return (records, failedRecords);
                    }
                }

                // تنفيذ الاستعلام وجلب البيانات
                var entities = await query.ToListAsync();

                // تحويل الكيانات إلى ViewModels
                records = _mapper.Map<List<TViewModel>>(entities);

                // إذا نجحت العملية، إزالة الأخطاء السابقة المرتبطة بها
                foreach (var failedRecord in failedRecords) ErrorManager.FailedRecords.Remove(failedRecord);

                return (records, failedRecords);
            }
            catch (Exception ex)
            {
                var failedRecord = ErrorHandler.CreateFailedRecord<TViewModel>(null, ex);
                failedRecords.Add(failedRecord);

                if (!ErrorManager.FailedRecords.Contains(failedRecord))
                    ErrorManager.FailedRecords.Add(failedRecord); // تسجيل الخطأ

                if (attempt == maxRetries) return (records, failedRecords);

                // تأخير قبل إعادة المحاولة
                await Task.Delay(1000);
            }

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