namespace Ralsei.Data;

public class Holder<T> where T : struct
{
    public T? Value { get; set; }
    
    public void Map(Action<T> action)
    {
        if (Value.HasValue)
            action(Value.Value);
    }
    
    public T GetValue()
        => Value ?? throw new InvalidOperationException("Value is not set");
}