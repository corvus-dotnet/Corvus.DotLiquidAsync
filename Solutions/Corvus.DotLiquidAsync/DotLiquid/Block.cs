// <copyright file="Block.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>
// Derived from code under the Apache 2 License from https://github.com/dotliquid/dotliquid

namespace DotLiquid
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using DotLiquid.Exceptions;
    using DotLiquid.Util;

    /// <summary>
    /// Represents a block in liquid:
    /// {% random 5 %} you have drawn number ^^^, lucky you! {% endrandom %}.
    /// </summary>
    public class Block : Tag
    {
        private static readonly Regex IsTag = R.B(@"^{0}", Liquid.TagStart);
        private static readonly Regex IsVariable = R.B(@"^{0}", Liquid.VariableStart);
        private static readonly Regex ContentOfVariable = R.B(@"^{0}(.*){1}$", Liquid.VariableStart, Liquid.VariableEnd);

        internal static readonly Regex FullToken = R.B(@"^{0}\s*(\w+)\s*(.*)?{1}$", Liquid.TagStart, Liquid.TagEnd);

        /// <summary>
        /// Parses a list of tokens.
        /// </summary>
        /// <param name="tokens"></param>
        protected override void Parse(List<string> tokens)
        {
            this.NodeList ??= new List<object>();
            this.NodeList.Clear();

            string token;
            while ((token = tokens.Shift()) != null)
            {
                Match isTagMatch = IsTag.Match(token);
                if (isTagMatch.Success)
                {
                    Match fullTokenMatch = FullToken.Match(token);
                    if (fullTokenMatch.Success)
                    {
                        // If we found the proper block delimitor just end parsing here and let the outer block
                        // proceed
                        if (this.BlockDelimiter == fullTokenMatch.Groups[1].Value)
                        {
                            this.EndTag();
                            return;
                        }

                        // Fetch the tag from registered blocks
                        Tag tag;
                        if ((tag = Template.CreateTag(fullTokenMatch.Groups[1].Value)) != null)
                        {
                            tag.Initialize(fullTokenMatch.Groups[1].Value, fullTokenMatch.Groups[2].Value, tokens);
                            this.NodeList.Add(tag);

                            // If the tag has some rules (eg: it must occur once) then check for them
                            tag.AssertTagRulesViolation(this.NodeList);
                        }
                        else
                        {
                            // This tag is not registered with the system
                            // pass it to the current block for special handling or error reporting
                            this.UnknownTag(fullTokenMatch.Groups[1].Value, fullTokenMatch.Groups[2].Value, tokens);
                        }
                    }
                    else
                    {
                        throw new SyntaxException(Liquid.ResourceManager.GetString("BlockTagNotTerminatedException"), token, Liquid.TagEnd);
                    }
                }
                else if (IsVariable.Match(token).Success)
                {
                    this.NodeList.Add(this.CreateVariable(token));
                }
                else if (token == string.Empty)
                {
                    // Pass
                }
                else
                {
                    this.NodeList.Add(token);
                }
            }

            // Make sure that its ok to end parsing in the current block.
            // Effectively this method will throw an exception unless the current block is
            // of type Document
            this.AssertMissingDelimitation();
        }

        /// <summary>
        /// Called at the end of the parsing of the tag.
        /// </summary>
        public virtual void EndTag()
        {
        }

        /// <summary>
        /// Handles an unknown tag.
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="markup"></param>
        /// <param name="tokens"></param>
        public virtual void UnknownTag(string tag, string markup, List<string> tokens)
        {
            SyntaxException exception = tag switch
            {
                "else" => new SyntaxException(Liquid.ResourceManager.GetString("BlockTagNoElseException"), this.BlockName),
                "end" => new SyntaxException(Liquid.ResourceManager.GetString("BlockTagNoEndException"), this.BlockName, this.BlockDelimiter),
                _ => new SyntaxException(Liquid.ResourceManager.GetString("BlockUnknownTagException"), tag),
            };

            throw exception;
        }

        /// <summary>
        /// Gets delimiter signaling the end of the block.
        /// </summary>
        /// <remarks>Usually "end"+block name.</remarks>
        protected virtual string BlockDelimiter
        {
            get { return string.Format("end{0}", this.BlockName); }
        }

        private string BlockName
        {
            get { return this.TagName; }
        }

        /// <summary>
        /// Creates a variable from a token:
        ///
        /// {{ variable }}.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public Variable CreateVariable(string token)
        {
            Match match = ContentOfVariable.Match(token);
            if (match.Success)
            {
                return new Variable(match.Groups[1].Value);
            }

            throw new SyntaxException(Liquid.ResourceManager.GetString("BlockVariableNotTerminatedException"), token, Liquid.VariableEnd);
        }

        /// <summary>
        /// Renders the block.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="result"></param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public override Task RenderAsync(Context context, TextWriter result)
        {
            return this.RenderAllAsync(this.NodeList, context, result);
        }

        /// <summary>
        /// Throw an exception if the block isn't closed.
        /// </summary>
        protected virtual void AssertMissingDelimitation()
        {
            throw new SyntaxException(Liquid.ResourceManager.GetString("BlockTagNotClosedException"), this.BlockName);
        }

        /// <summary>
        /// Renders all the objects in the list.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="context"></param>
        /// <param name="result"></param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        protected async Task RenderAllAsync(List<object> list, Context context, TextWriter result)
        {
            foreach (object token in list)
            {
                context.CheckTimeout();

                try
                {
                    if (token is IRenderable renderableToken)
                    {
                        await renderableToken.RenderAsync(context, result).ConfigureAwait(false);
                    }
                    else
                    {
                        await result.WriteAsync(token.ToString()).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.InnerException is LiquidException)
                    {
                        ex = ex.InnerException;
                    }

                    await result.WriteAsync(context.HandleError(ex)).ConfigureAwait(false);
                }
            }
        }
    }
}
