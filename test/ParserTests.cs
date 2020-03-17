using System;
using System.Linq;
using System.Text.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetShell;

namespace Tests
{
    class TestCommands
    {

    }

    [TestClass]
    public class ParserTests
    {
        RpcDispatcher Rpc = new RpcDispatcher(new TestCommands());

        static string[] Params(params string[] array) => array;
        static string[] UnquoteAll(string[] args) => Array.ConvertAll(args, s => s.Trim('"'));

        [TestMethod]
        public void SimpleParameters()
        {
            Assert.IsTrue(Rpc.TryParse("echo hi", out var args));
            CollectionAssert.AreEqual(Params("echo", "hi"), args);
        }

        [TestMethod]
        public void QuotedParameters()
        {
            Assert.IsTrue(Rpc.TryParse("echo \"hi\"", out var args));
            CollectionAssert.AreEqual(Params("echo", "hi"), UnquoteAll(args));
        }

        [TestMethod]
        public void QuotedSpacedParameters()
        {
            Assert.IsTrue(Rpc.TryParse("echo \"Hello World\"", out var args));
            CollectionAssert.AreEqual(Params("echo", "Hello World"), UnquoteAll(args));
        }

        [TestMethod]
        public void QuotedEscapedParameters()
        {
            Assert.IsTrue(Rpc.TryParse("echo \"Hello \"\"World\"\"!\"", out var args));
            CollectionAssert.AreEqual(Params("echo", @"Hello ""World""!"), UnquoteAll(args));
        }

        [TestMethod]
        public void NotClosedQuotes()
        {
            Assert.IsFalse(Rpc.TryParse("echo \"hi", out var args));
        }

    }
}
