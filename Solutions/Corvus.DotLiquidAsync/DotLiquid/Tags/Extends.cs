// <copyright file="Extends.cs" company="Endjin Limited">
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
    using DotLiquid.FileSystems;
    using DotLiquid.Util;

    /// <summary>
    /// The Extends tag is used in conjunction with the Block tag to provide template inheritance.
    /// For further syntax and usage please refer to <a href="http://docs.djangoproject.com/en/dev/topics/templates/#template-inheritance"/>.
    /// </summary>
    /// <example>
    /// To see how Extends and Block can be used together, start by considering this example:
    ///
    /// <html>
    /// <head>
    ///   <title>{% block title %}My Website{% endblock %}</title>
    /// </head>
    ///
    /// <body>
    ///   <div id="sidebar">
    ///     {% block sidebar %}
    ///     <ul>
    ///       <li><a href="/">Home</a></li>
    ///       <li><a href="/blog/">Blog</a></li>
    ///     </ul>
    ///     {% endblock %}
    ///   </div>
    ///
    ///   <div id="content">
    ///     {% block content %}{% endblock %}
    ///   </div>
    /// </body>
    /// </html>
    ///
    /// We'll assume this is saved in a file called base.html. In ASP.NET MVC terminology, this file would
    /// be the master page or layout, and each of the "blocks" would be a section. Child templates
    /// (in ASP.NET MVC terminology, views) fill or override these blocks with content. If a child template
    /// does not define a particular block, then the content from the parent template is used as a fallback.
    ///
    /// A child template might look like this:
    ///
    /// {% extends "base.html" %}
    /// {% block title %}My AMAZING Website{% endblock %}
    ///
    /// {% block content %}
    /// {% for entry in blog_entries %}
    ///   <h2>{{ entry.title }}</h2>
    ///   <p>{{ entry.body }}</p>
    /// {% endfor %}
    /// {% endblock %}
    ///
    /// The current IFileSystem will be used to locate "base.html".
    /// </example>
    public class Extends : DotLiquid.Block
    {
        private static readonly Regex Syntax = R.B(@"^({0})", Liquid.QuotedFragment);

        private string templateName;

        public override void Initialize(string tagName, string markup, List<string> tokens)
        {
            Match syntaxMatch = Syntax.Match(markup);

            if (syntaxMatch.Success)
            {
                this.templateName = syntaxMatch.Groups[1].Value;
            }
            else
            {
                throw new SyntaxException(Liquid.ResourceManager.GetString("ExtendsTagSyntaxException"));
            }

            base.Initialize(tagName, markup, tokens);
        }

        internal override void AssertTagRulesViolation(List<object> rootNodeList)
        {
            if (!(rootNodeList[0] is Extends))
            {
                throw new SyntaxException(Liquid.ResourceManager.GetString("ExtendsTagMustBeFirstTagException"));
            }

            this.NodeList.ForEach(n =>
            {
                if (!((n is string && ((string)n).IsNullOrWhiteSpace()) || n is Block || n is Comment || n is Extends))
                {
                    throw new SyntaxException(Liquid.ResourceManager.GetString("ExtendsTagUnallowedTagsException"));
                }
            });

            if (this.NodeList.Count(o => o is Extends) > 0)
            {
                throw new SyntaxException(Liquid.ResourceManager.GetString("ExtendsTagCanBeUsedOneException"));
            }
        }

        protected override void AssertMissingDelimitation()
        {
        }

        public async override Task RenderAsync(Context context, TextWriter result)
        {
            // Get the template or template content and then either copy it (since it will be modified) or parse it
            IFileSystem fileSystem = context.Registers["file_system"] as IFileSystem ?? Template.FileSystem;
            Template template = null;
            if (fileSystem is ITemplateFileSystem templateFileSystem)
            {
                template = await templateFileSystem.GetTemplateAsync(context, this.templateName).ConfigureAwait(false);
            }

            if (template == null)
            {
                string source = await fileSystem.ReadTemplateFileAsync(context, this.templateName).ConfigureAwait(false);
                template = Template.Parse(source);
            }

            List<Block> parentBlocks = this.FindBlocks(template.Root, null);
            List<Block> orphanedBlocks = ((List<Block>)context.Scopes[0]["extends"]) ?? new List<Block>();
            BlockRenderState blockState = BlockRenderState.Find(context) ?? new BlockRenderState();

            await context.Stack(() =>
            {
                context["blockstate"] = blockState;         // Set or copy the block state down to this scope
                context["extends"] = new List<Block>();     // Holds Blocks that were not found in the parent
                foreach (Block block in this.NodeList.OfType<Block>().Concat(orphanedBlocks))
                {
                    Block pb = parentBlocks.Find(b => b.BlockName == block.BlockName);

                    if (pb != null)
                    {
                        if (blockState.Parents.TryGetValue(block, out Block parent))
                        {
                            blockState.Parents[pb] = parent;
                        }

                        pb.AddParent(blockState.Parents, pb.GetNodeList(blockState));
                        blockState.NodeLists[pb] = block.GetNodeList(blockState);
                    }
                    else if (this.IsExtending(template))
                    {
                        ((List<Block>)context.Scopes[0]["extends"]).Add(block);
                    }
                }

                return template.RenderAsync(result, RenderParameters.FromContext(context, result.FormatProvider));
            }).ConfigureAwait(false);
        }

        public bool IsExtending(Template template)
        {
            return template.Root.NodeList.Any(node => node is Extends);
        }

        private List<Block> FindBlocks(object node, List<Block> blocks)
        {
            if (blocks == null)
            {
                blocks = new List<Block>();
            }

            if (node.RespondTo("NodeList"))
            {
                var nodeList = (List<object>)node.Send("NodeList");

                if (nodeList != null)
                {
                    nodeList.ForEach(n =>
                    {
                        if (n is Block block)
                        {
                            if (blocks.All(bl => bl.BlockName != block.BlockName))
                            {
                                blocks.Add(block);
                            }
                        }

                        this.FindBlocks(n, blocks);
                    });
                }
            }

            return blocks;
        }
    }
}
