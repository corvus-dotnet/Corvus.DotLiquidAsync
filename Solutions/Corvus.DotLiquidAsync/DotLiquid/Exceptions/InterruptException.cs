// <copyright file="InterruptException.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>
// Derived from code under the Apache 2 License from https://github.com/dotliquid/dotliquid

namespace DotLiquid.Exceptions
{
    public class InterruptException : LiquidException
    {
        public InterruptException(string message)
            : base(message)
        {
        }
    }

    public class BreakInterrupt : InterruptException
    {
        public BreakInterrupt()
            : base("Misplaced 'break' statement")
        {
        }
    }

    public class ContinueInterrupt : InterruptException
    {
        public ContinueInterrupt()
            : base("Misplaced 'continue' statement")
        {
        }
    }
}
