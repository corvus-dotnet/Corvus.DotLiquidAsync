namespace DotLiquid.Tests.Tags
{
    using DotLiquid.Exceptions;
    using DotLiquid.FileSystems;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;

    [TestFixture]
    public class IncludeTagTests
    {
        private class TestFileSystem : IFileSystem
        {
            public Task<string> ReadTemplateFileAsync(Context context, string templateName)
            {
                string templatePath = (string)context[templateName];

                return templatePath switch
                {
                    "product" => Task.FromResult("Product: {{ product.title }} "),

                    "locale_variables" => Task.FromResult("Locale: {{echo1}} {{echo2}}"),

                    "variant" => Task.FromResult("Variant: {{ variant.title }}"),

                    "nested_template" => Task.FromResult("{% include 'header' %} {% include 'body' %} {% include 'footer' %}"),

                    "body" => Task.FromResult("body {% include 'body_detail' %}"),

                    "nested_product_template" => Task.FromResult("Product: {{ nested_product_template.title }} {%include 'details'%} "),

                    "recursively_nested_template" => Task.FromResult("-{% include 'recursively_nested_template' %}"),

                    "pick_a_source" => Task.FromResult("from TestFileSystem"),

                    _ => Task.FromResult(templatePath),
                };
            }
        }

        internal class TestTemplateFileSystem : ITemplateFileSystem
        {
            private readonly IDictionary<string, Template> _templateCache = new Dictionary<string, Template>();
            private readonly IFileSystem _baseFileSystem = null;

            public int CacheHitTimes { get; private set; }

            public TestTemplateFileSystem(IFileSystem baseFileSystem)
            {
                this._baseFileSystem = baseFileSystem;
            }

            public Task<string> ReadTemplateFileAsync(Context context, string templateName)
            {
                return this._baseFileSystem.ReadTemplateFileAsync(context, templateName);
            }

            public async Task<Template> GetTemplateAsync(Context context, string templateName)
            {
                if (this._templateCache.TryGetValue(templateName, out Template template))
                {
                    ++this.CacheHitTimes;
                    return template;
                }
                string result = await this.ReadTemplateFileAsync(context, templateName).ConfigureAwait(false);
                template = Template.Parse(result);
                this._templateCache[templateName] = template;
                return template;
            }
        }

        private class OtherFileSystem : IFileSystem
        {
            public Task<string> ReadTemplateFileAsync(Context context, string templateName)
            {
                return Task.FromResult("from OtherFileSystem");
            }
        }

        private class InfiniteFileSystem : IFileSystem
        {
            public Task<string> ReadTemplateFileAsync(Context context, string templateName)
            {
                return Task.FromResult("-{% include 'loop' %}");
            }
        }

        [SetUp]
        public void SetUp()
        {
            Template.FileSystem = new TestFileSystem();
        }

        [Test]
        public void TestIncludeTagMustNotBeConsideredError()
        {
            Assert.AreEqual(0, Template.Parse("{% include 'product_template' %}").Errors.Count);
            Assert.DoesNotThrow(() => Template.Parse("{% include 'product_template' %}").RenderAsync(new RenderParameters(CultureInfo.InvariantCulture) { ErrorsOutputMode = ErrorsOutputMode.Rethrow }).GetAwaiter().GetResult());
        }

        [Test]
        public async Task TestIncludeTagLooksForFileSystemInRegistersFirst()
        {
            Assert.AreEqual("from OtherFileSystem", await Template.Parse("{% include 'pick_a_source' %}").RenderAsync(new RenderParameters(CultureInfo.InvariantCulture) { Registers = Hash.FromAnonymousObject(new { file_system = new OtherFileSystem() }) }));
        }

        [Test]
        public async Task TestIncludeTagWith()
        {
            Assert.AreEqual("Product: Draft 151cm ", await Template.Parse("{% include 'product' with products[0] %}").RenderAsync(Hash.FromAnonymousObject(new { products = new[] { Hash.FromAnonymousObject(new { title = "Draft 151cm" }), Hash.FromAnonymousObject(new { title = "Element 155cm" }) } })));
        }

        [Test]
        public async Task TestIncludeTagWithDefaultName()
        {
            Assert.AreEqual("Product: Draft 151cm ", await Template.Parse("{% include 'product' %}").RenderAsync(Hash.FromAnonymousObject(new { product = Hash.FromAnonymousObject(new { title = "Draft 151cm" }) })));
        }

        [Test]
        public async Task TestIncludeTagFor()
        {
            Assert.AreEqual("Product: Draft 151cm Product: Element 155cm ", await Template.Parse("{% include 'product' for products %}").RenderAsync(Hash.FromAnonymousObject(new { products = new[] { Hash.FromAnonymousObject(new { title = "Draft 151cm" }), Hash.FromAnonymousObject(new { title = "Element 155cm" }) } })));
        }

        [Test]
        public async Task TestIncludeTagWithLocalVariables()
        {
            Assert.AreEqual("Locale: test123 ", await Template.Parse("{% include 'locale_variables' echo1: 'test123' %}").RenderAsync());
        }

        [Test]
        public async Task TestIncludeTagWithMultipleLocalVariables()
        {
            Assert.AreEqual("Locale: test123 test321", await Template.Parse("{% include 'locale_variables' echo1: 'test123', echo2: 'test321' %}").RenderAsync());
        }

        [Test]
        public async Task TestIncludeTagWithMultipleLocalVariablesFromContext()
        {
            Assert.AreEqual("Locale: test123 test321",
                await Template.Parse("{% include 'locale_variables' echo1: echo1, echo2: more_echos.echo2 %}").RenderAsync(Hash.FromAnonymousObject(new { echo1 = "test123", more_echos = Hash.FromAnonymousObject(new { echo2 = "test321" }) })));
        }

        [Test]
        public async Task TestNestedIncludeTag()
        {
            Assert.AreEqual("body body_detail", await Template.Parse("{% include 'body' %}").RenderAsync());

            Assert.AreEqual("header body body_detail footer", await Template.Parse("{% include 'nested_template' %}").RenderAsync());
        }

        [Test]
        public async Task TestNestedIncludeTagWithVariable()
        {
            Assert.AreEqual("Product: Draft 151cm details ",
                await Template.Parse("{% include 'nested_product_template' with product %}").RenderAsync(Hash.FromAnonymousObject(new { product = Hash.FromAnonymousObject(new { title = "Draft 151cm" }) })));

            Assert.AreEqual("Product: Draft 151cm details Product: Element 155cm details ",
                await Template.Parse("{% include 'nested_product_template' for products %}").RenderAsync(Hash.FromAnonymousObject(new { products = new[] { Hash.FromAnonymousObject(new { title = "Draft 151cm" }), Hash.FromAnonymousObject(new { title = "Element 155cm" }) } })));
        }

        [Test]
        public void TestRecursivelyIncludedTemplateDoesNotProductEndlessLoop()
        {
            Template.FileSystem = new InfiniteFileSystem();

            Assert.Throws<StackLevelException>(() => Template.Parse("{% include 'loop' %}").RenderAsync(new RenderParameters(CultureInfo.InvariantCulture) { ErrorsOutputMode = ErrorsOutputMode.Rethrow }).GetAwaiter().GetResult());
        }

        [Test]
        public async Task TestDynamicallyChosenTemplate()
        {
            Assert.AreEqual("Test123", await Template.Parse("{% include template %}").RenderAsync(Hash.FromAnonymousObject(new { template = "Test123" })));
            Assert.AreEqual("Test321", await Template.Parse("{% include template %}").RenderAsync(Hash.FromAnonymousObject(new { template = "Test321" })));

            Assert.AreEqual("Product: Draft 151cm ", await Template.Parse("{% include template for product %}").RenderAsync(Hash.FromAnonymousObject(new { template = "product", product = Hash.FromAnonymousObject(new { title = "Draft 151cm" }) })));
        }

        [Test]
        public async Task TestUndefinedTemplateVariableWithTestFileSystem()
        {
            Assert.AreEqual(" hello  world ", await Template.Parse(" hello {% include notthere %} world ").RenderAsync());
        }

        [Test]
        public void TestUndefinedTemplateVariableWithLocalFileSystem()
        {
            Template.FileSystem = new LocalFileSystem(string.Empty);
            Assert.Throws<FileSystemException>(() => Template.Parse(" hello {% include notthere %} world ").RenderAsync(new RenderParameters(CultureInfo.InvariantCulture)
            {
                ErrorsOutputMode = ErrorsOutputMode.Rethrow
            }).GetAwaiter().GetResult());
        }

        [Test]
        public void TestMissingTemplateWithLocalFileSystem()
        {
            Template.FileSystem = new LocalFileSystem(string.Empty);
            Assert.Throws<FileSystemException>(() => Template.Parse(" hello {% include 'doesnotexist' %} world ").RenderAsync(new RenderParameters(CultureInfo.InvariantCulture)
            {
                ErrorsOutputMode = ErrorsOutputMode.Rethrow
            }).GetAwaiter().GetResult());
        }

        [Test]
        public async Task TestIncludeFromTemplateFileSystem()
        {
            var fileSystem = new TestTemplateFileSystem(new TestFileSystem());
            Template.FileSystem = fileSystem;
            for (int i = 0; i < 2; ++i)
            {
                Assert.AreEqual("Product: Draft 151cm ", await Template.Parse("{% include 'product' with products[0] %}").RenderAsync(Hash.FromAnonymousObject(new { products = new[] { Hash.FromAnonymousObject(new { title = "Draft 151cm" }), Hash.FromAnonymousObject(new { title = "Element 155cm" }) } })));
            }
            Assert.AreEqual(fileSystem.CacheHitTimes, 1);
        }
    }
}
