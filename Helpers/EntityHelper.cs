
using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using MiniProjectDesigner.ViewModels;

namespace MiniProjectDesigner.Helpers
{
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
            // ≈–« ﬂ«‰  «·ﬁÌ„… ﬂ«∆‰ ViewModel° ﬁ„ »«” Œ—«Ã ﬁÌ„… «·„› «Õ «·√”«”Ì
            if (primaryKeyValue != null && primaryKeyValue.GetType().GetProperty(primaryKeyName) != null)
                primaryKeyValue = primaryKeyValue.GetType().GetProperty(primaryKeyName)?.GetValue(primaryKeyValue)
                                  ?? throw new InvalidOperationException(
                                      "Failed to extract primary key value from ViewModel.");

            var parameter = Expression.Parameter(entityType, "entity");
            var propertyAccess = Expression.Property(parameter, primaryKeyName);

            // «· √ﬂœ „‰ „ÿ«»ﬁ… «·‰Ê⁄ Ê ÕÊÌ· «·ﬁÌ„… ≈–« ·“„ «·√„—
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
}
