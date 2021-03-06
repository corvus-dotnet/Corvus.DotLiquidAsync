// <copyright file="For.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>
// Derived from code under the Apache 2 License from https://github.com/dotliquid/dotliquid

namespace DotLiquid.Tags
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using DotLiquid.Exceptions;
    using DotLiquid.Util;

    /// <summary>
    /// "For" iterates over an array or collection.
    /// Several useful variables are available to you within the loop.
    ///
    /// == Basic usage:
    ///    {% for item in collection %}
    ///      {{ forloop.index }}: {{ item.name }}
    ///    {% endfor %}
    ///
    /// == Advanced usage:
    ///    {% for item in collection %}
    ///      &lt;div {% if forloop.first %}class="first"{% endif %}&gt;
    ///        Item {{ forloop.index }}: {{ item.name }}
    ///      &lt;/div&gt;
    ///    {% endfor %}
    ///
    /// You can also define a limit and offset much like SQL.  Remember
    /// that offset starts at 0 for the first item.
    ///
    ///    {% for item in collection limit:5 offset:10 %}
    ///      {{ item.name }}
    ///    {% end %}
    ///
    ///  To reverse the for loop simply use {% for item in collection reversed %}
    ///
    /// == Available variables:
    ///
    /// forloop.name:: 'item-collection'
    /// forloop.length:: Length of the loop
    /// forloop.index:: The current item's position in the collection;
    ///                 forloop.index starts at 1.
    ///                 This is helpful for non-programmers who start believe
    ///                 the first item in an array is 1, not 0.
    /// forloop.index0:: The current item's position in the collection
    ///                  where the first item is 0
    /// forloop.rindex:: Number of items remaining in the loop
    ///                  (length - index) where 1 is the last item.
    /// forloop.rindex0:: Number of items remaining in the loop
    ///                   where 0 is the last item.
    /// forloop.first:: Returns true if the item is the first item.
    /// forloop.last:: Returns true if the item is the last item.
    /// </summary>
    public class For : DotLiquid.Block
    {
        private static readonly Regex Syntax = R.B(R.Q(@"(\w+)\s+in\s+({0}+)\s*(reversed)?"), Liquid.QuotedFragment);
        private static readonly string ForTagMaxIterationsExceededException = Liquid.ResourceManager.GetString("ForTagMaximumIterationsExceededException");

        private string variableName;
        private string collectionName;
        private string name;
        private bool reversed;
        private Dictionary<string, string> attributes;

        /// <summary>
        /// Initializes the for tag.
        /// </summary>
        /// <param name="tagName">Name of the parsed tag.</param>
        /// <param name="markup">Markup of the parsed tag.</param>
        /// <param name="tokens">Toeksn of the parsed tag.</param>
        public override void Initialize(string tagName, string markup, List<string> tokens)
        {
            Match match = Syntax.Match(markup);
            if (match.Success)
            {
                this.variableName = match.Groups[1].Value;
                this.collectionName = match.Groups[2].Value;
                this.name = string.Format("{0}-{1}", this.variableName, this.collectionName);
                this.reversed = !string.IsNullOrEmpty(match.Groups[3].Value);
                this.attributes = new Dictionary<string, string>(Template.NamingConvention.StringComparer);
                R.Scan(
                    markup,
                    Liquid.TagAttributes,
                    (key, value) => this.attributes[key] = value);
            }
            else
            {
                throw new SyntaxException(Liquid.ResourceManager.GetString("ForTagSyntaxException"));
            }

            base.Initialize(tagName, markup, tokens);
        }

        /// <summary>
        /// Renders the for tag.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="result"></param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async override Task RenderAsync(Context context, TextWriter result)
        {
            context.Registers["for"] = context.Registers["for"] ?? new Hash(0);

            object collection = context[this.collectionName];

            if (!(collection is IEnumerable))
            {
                return;
            }

            int from = this.attributes.ContainsKey("offset")
                ? (this.attributes["offset"] == "continue")
                    ? Convert.ToInt32(context.Registers.Get<Hash>("for")[this.name])
                    : Convert.ToInt32(context[this.attributes["offset"]])
                : 0;

            int? limit = this.attributes.ContainsKey("limit") ? context[this.attributes["limit"]] as int? : null;
            int? to = (limit != null) ? (int?)(limit.Value + from) : null;

            List<object> segment = SliceCollectionUsingEach(context, (IEnumerable)collection, from, to);

            if (!segment.Any())
            {
                return;
            }

            if (this.reversed)
            {
                segment.Reverse();
            }

            int length = segment.Count;

            // Store our progress through the collection for the continue flag
            context.Registers.Get<Hash>("for")[this.name] = from + length;

            await context.Stack(async () =>
            {
                for (int index = 0; index < segment.Count; index++)
                {
                    context.CheckTimeout();

                    object item = segment[index];
                    if (item is KeyValuePair<string, object>)
                    {
                        string itemKey = ((KeyValuePair<string, object>)item).Key;
                        object itemValue = ((KeyValuePair<string, object>)item).Value;
                        this.BuildContext(context, this.variableName, itemKey, itemValue);
                    }
                    else
                    {
                        context[this.variableName] = item;
                    }

                    context["forloop"] = Hash.FromDictionary(
                        new Dictionary<string, object>
                        {
                            ["name"] = this.name,
                            ["length"] = length,
                            ["index"] = index + 1,
                            ["index0"] = index,
                            ["rindex"] = length - index,
                            ["rindex0"] = length - index - 1,
                            ["first"] = index == 0,
                            ["last"] = index == length - 1,
                        });
                    try
                    {
                        await this.RenderAllAsync(this.NodeList, context, result).ConfigureAwait(false);
                    }
                    catch (BreakInterrupt)
                    {
                        break;
                    }
                    catch (ContinueInterrupt)
                    {
                        // ContinueInterrupt is used only to skip the current value but not to stop the iteration
                    }
                }
            }).ConfigureAwait(false);
        }

        private static List<object> SliceCollectionUsingEach(Context context, IEnumerable collection, int from, int? to)
        {
            var segments = new List<object>();
            int index = 0;
            foreach (object item in collection)
            {
                context.CheckTimeout();

                if (to != null && to.Value <= index)
                {
                    break;
                }

                if (from <= index)
                {
                    segments.Add(item);
                }

                ++index;

                if (context.MaxIterations > 0 && index > context.MaxIterations)
                {
                    throw new MaximumIterationsExceededException(For.ForTagMaxIterationsExceededException, context.MaxIterations.ToString());
                }
            }

            return segments;
        }

        private void BuildContext(Context context, string parent, string key, object value)
        {
            if (value is Hash hashValue)
            {
                hashValue["itemName"] = key;
                context[parent] = value;

                foreach (KeyValuePair<string, object> hashItem in (Hash)value)
                {
                    if (hashItem.Value is Hash)
                    {
                        this.BuildContext(context, parent + "." + key, hashItem.Key, hashItem.Value);
                    }
                }
            }
        }
    }
}
