using System;
using System.Collections.Generic;

namespace Smithers.Worker.Jobs
{
    public static class ErrorTracking
    {
        private static readonly Dictionary<Type, int> Failures = new Dictionary<Type, int>();

        public static void IncreaseFailCount<T>()
        {
            if (Failures.ContainsKey(typeof(T)))
            {
                Failures[typeof(T)]++;
            }
            else
            {
                Failures.Add(typeof(T), 1);
            }
        }

        public static void ResetFailCount<T>()
        {
            if (Failures.ContainsKey(typeof(T)))
            {
                Failures[typeof(T)] = 0;
            }
        }

        public static int GetFailCount<T>()
        {
            return Failures.ContainsKey(typeof(T)) ? Failures[typeof(T)] : 0;
        }
    }
}