namespace MediFlow.Infrastructure.Data;

using Dapper;
using System.Data;

/// <summary>
/// Maps SQL Server <c>date</c> columns (returned as DateTime) to <see cref="DateOnly"/>.
/// Registered once at startup in AddMediFlowInfrastructure.
/// </summary>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) => value switch
    {
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        DateOnly dateOnly => dateOnly,
        _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to DateOnly."),
    };

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value;
    }
}
