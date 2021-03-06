// <copyright file="ILiquidizable.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>
// Derived from code under the Apache 2 License from https://github.com/dotliquid/dotliquid

namespace DotLiquid
{
    /// <summary>
    /// See here for motivation: <a href="http://wiki.github.com/tobi/liquid/using-liquid-without-rails"/>.
    /// This allows for extra security by only giving the template access to the specific
    /// variables you want it to have access to.
    /// </summary>
    public interface ILiquidizable
    {
        object ToLiquid();
    }
}
