using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace SmartUnit
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AssertionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class AssertionSetAttribute : Attribute
    {
        public Type AssertionSetType { get; }

        public AssertionSetAttribute(Type assertionSetType)
        {
            if (assertionSetType.BaseType != typeof(AssertionSet))
            {
                throw new ArgumentException(nameof(assertionSetType));
            }

            AssertionSetType = assertionSetType;
        }
    }

    public abstract class AssertionSet : ServiceCollection
    {
        public abstract void Configure();
    }

    internal class AssertionException : Exception { }

    public static class AssertExtensions
    {
        public static object AssertThat(this object obj, Func<object, bool> assertion)
        {
            if (assertion(obj))
            {
                return obj;
            }

            throw new AssertionException();
        }

        public static async Task<object> AssertThatAsync(this object obj, Func<object, Task<bool>> assertion)
        {
            if (await assertion(obj))
            {
                return obj;
            }

            throw new AssertionException();
        }
        public static object AssertException<TException>(this object obj, Func<object, bool> assertion) where TException : Exception
        {
            try
            {
                assertion(obj);
                throw new AssertionException();
            }
            catch (TException)
            {
                return obj;
            }
        }

        public static async Task<object> AssertThatAsync<TException>(this object obj, Func<object, Task<bool>> assertion) where TException : Exception
        {
            try
            {
                await assertion(obj);
                throw new AssertionException();
            }
            catch (TException)
            {
                return obj;
            }
        }
    }
}
