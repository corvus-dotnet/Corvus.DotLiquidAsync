// <copyright file="Block.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace DotLiquid.Tags
{
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using DotLiquid.Exceptions;
    using DotLiquid.Util;

    public class BlockDrop : Drop
    {
        private readonly Block block;
        private readonly TextWriter result;

        public BlockDrop(Block block, TextWriter result)
        {
            this.block = block;
            this.result = result;
        }

        public Task Super()
        {
            return this.block.CallSuperAsync(this.Context, this.result);
        }
    }

    // Keeps track of the render-time state of all Blocks for a given Context
    internal class BlockRenderState
    {
        public Dictionary<Block, Block> Parents { get; private set; }

        public Dictionary<Block, List<object>> NodeLists { get; private set; }

        public BlockRenderState()
        {
            this.Parents = new Dictionary<Block, Block>();
            this.NodeLists = new Dictionary<Block, List<object>>();
        }

        public List<object> GetNodeList(Block block)
        {
            if (!this.NodeLists.TryGetValue(block, out List<object> nodeList))
            {
                nodeList = block.NodeList;
            }

            return nodeList;
        }

        // Searches up the scopes for the inner-most BlockRenderState (though there should be only one)
        public static BlockRenderState Find(Context context)
        {
            foreach (Hash scope in context.Scopes)
            {
                if (scope.TryGetValue("blockstate", out object blockState))
                {
                    return blockState as BlockRenderState;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The Block tag is used in conjunction with the Extends tag to provide template inheritance.
    /// For an example please refer to the Extends tag.
    /// </summary>
    public class Block : DotLiquid.Block
    {
        private static readonly Regex Syntax = R.C(@"(\w+)");

        internal string BlockName { get; set; }

        public override void Initialize(string tagName, string markup, List<string> tokens)
        {
            Match syntaxMatch = Syntax.Match(markup);
            if (syntaxMatch.Success)
            {
                this.BlockName = syntaxMatch.Groups[1].Value;
            }
            else
            {
                throw new SyntaxException(Liquid.ResourceManager.GetString("BlockTagSyntaxException"));
            }

            if (tokens != null)
            {
                base.Initialize(tagName, markup, tokens);
            }
        }

        internal override void AssertTagRulesViolation(List<object> rootNodeList)
        {
            rootNodeList.ForEach(n =>
                {
                    var b1 = n as Block;

                    if (b1 != null)
                    {
                        List<object> found = rootNodeList.FindAll(o =>
                            {
                                var b2 = o as Block;
                                return b2 != null && b1.BlockName == b2.BlockName;
                            });

                        if (found != null && found.Count > 1)
                        {
                            throw new SyntaxException(Liquid.ResourceManager.GetString("BlockTagAlreadyDefinedException"), b1.BlockName);
                        }
                    }
                });
        }

        public override Task RenderAsync(Context context, TextWriter result)
        {
            var blockState = BlockRenderState.Find(context);
            return context.Stack(() =>
                {
                    context["block"] = new BlockDrop(this, result);
                    return this.RenderAllAsync(this.GetNodeList(blockState), context, result);
                });
        }

        // Gets the render-time node list from the node state
        internal List<object> GetNodeList(BlockRenderState blockState)
        {
            return blockState == null ? this.NodeList : blockState.GetNodeList(this);
        }

        public void AddParent(Dictionary<Block, Block> parents, List<object> nodeList)
        {
            if (parents.TryGetValue(this, out Block parent))
            {
                parent.AddParent(parents, nodeList);
            }
            else
            {
                parent = new Block();
                parent.Initialize(this.TagName, this.BlockName, null);
                parent.NodeList = new List<object>(nodeList);
                parents[this] = parent;
            }
        }

        public async Task CallSuperAsync(Context context, TextWriter result)
        {
            var blockState = BlockRenderState.Find(context);
            if (blockState != null
    && blockState.Parents.TryGetValue(this, out Block parent)
    && parent != null)
            {
                await parent.RenderAsync(context, result).ConfigureAwait(false);
            }
        }
    }
}
