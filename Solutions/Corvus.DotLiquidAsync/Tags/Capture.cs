// <copyright file="Capture.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>
// Derived from code under the Apache 2 License from https://github.com/dotliquid/dotliquid

namespace DotLiquid.Tags
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using DotLiquid.Exceptions;
    using DotLiquid.Util;

    /// <summary>
    /// Capture stores the result of a block into a variable without rendering it inplace.
    ///
    /// {% capture heading %}
    /// Monkeys!
    /// {% endcapture %}
    /// ...
    /// <h1>{{ heading }}</h1>
    ///
    /// Capture is useful for saving content for use later in your template, such as
    /// in a sidebar or footer.
    /// </summary>
    public class Capture : DotLiquid.Block
    {
        private static readonly Regex Syntax = R.C(@"(\w+)");

        private string to;

        public override void Initialize(string tagName, string markup, List<string> tokens)
        {
            Match syntaxMatch = Syntax.Match(markup);
            if (syntaxMatch.Success)
            {
                this.to = syntaxMatch.Groups[1].Value;
            }
            else
            {
                throw new SyntaxException(Liquid.ResourceManager.GetString("CaptureTagSyntaxException"));
            }

            base.Initialize(tagName, markup, tokens);
        }

        public async override Task RenderAsync(Context context, TextWriter result)
        {
            using TextWriter temp = new StringWriter(result.FormatProvider);
            await base.RenderAsync(context, temp).ConfigureAwait(false);
            context.Scopes.Last()[this.to] = temp.ToString();
        }
    }
}
