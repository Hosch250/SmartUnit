using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace SmartUnit
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AssertionAttribute : Attribute
    {
        public string? Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class AssertionSetAttribute : Attribute
    {
        public Type AssertionSetType { get; }

        public AssertionSetAttribute(Type assertionSetType)
        {
            if (assertionSetType.BaseType != typeof(AssertionSet))
            {
                throw new ArgumentException(null, nameof(assertionSetType));
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
        public static T AssertThat<T>(this T obj, Func<T, bool> assertion)
        {
            if (assertion(obj))
            {
                return obj;
            }

            throw new AssertionException();
        }

        public static async Task<T> AssertThatAsync<T>(this T obj, Func<T, Task<bool>> assertion)
        {
            if (await assertion(obj))
            {
                return obj;
            }

            throw new AssertionException();
        }

        public static T AssertException<T, TException>(this T obj, Action<T> assertion) where TException : Exception
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

        public static async Task<T> AssertThatExceptionAsync<T, TException>(this T obj, Func<T, Task> assertion) where TException : Exception
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
