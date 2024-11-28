
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

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

        public static LambdaExpression BuildPrimaryKeyFilter(Type entityType, string primaryKeyName, object primaryKeyValue)
        {
            var parameter = Expression.Parameter(entityType, "entity");
            var propertyAccess = Expression.Property(parameter, primaryKeyName);
            var propertyType = propertyAccess.Type;
            var convertedValue = Convert.ChangeType(primaryKeyValue, propertyType);
            var constantValue = Expression.Constant(convertedValue, propertyType);
            var equality = Expression.Equal(propertyAccess, constantValue);
            return Expression.Lambda(equality, parameter);
        }
    }
}
