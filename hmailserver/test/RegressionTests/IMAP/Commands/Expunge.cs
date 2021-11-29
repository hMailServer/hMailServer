// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Text;
using hMailServer;
using NUnit.Framework;
using RegressionTests.Shared;

namespace RegressionTests.IMAP.Commands
{
   [TestFixture]
   public class Expunge : TestFixtureBase
   {
      [Test]
      public void TestNormalExpunge()
      {
         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "ExpungeAccount@test.com",
                                                                            "test");

         for (int i = 0; i < 3; i++)
            SmtpClientSimulator.StaticSend("test@test.com", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 3);

         var simulator = new ImapClientSimulator();
         Assert.IsTrue(simulator.ConnectAndLogon(account.Address, "test"));
         Assert.IsTrue(simulator.SelectFolder("Inbox"));

         Assert.IsTrue(simulator.SetFlagOnMessage(1, true, @"\Deleted"));
         Assert.IsTrue(simulator.SetFlagOnMessage(3, true, @"\Deleted"));

         string result;
         Assert.IsTrue(simulator.Expunge(out result));

         // Messages 1 and 2 should be deleted. 2, because when the first message
         // is deleted, the index of the message which was originally 3, is now 2.
         Assert.IsTrue(result.Contains("* 1 EXPUNGE\r\n* 2 EXPUNGE"));
      }

      [Test]
      [Description("Assert that the EXPUNGE command works")]
      public void TestUidExpunge_NoMessageFlaggedWithDelete()
      {
         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "ExpungeAccount@test.com",
            "test");

         for (int i = 0; i < 3; i++)
            SmtpClientSimulator.StaticSend("test@test.com", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 3);

         var simulator = new ImapClientSimulator();
         Assert.IsTrue(simulator.ConnectAndLogon(account.Address, "test"));
         Assert.IsTrue(simulator.SelectFolder("Inbox"));

         var response = simulator.SendSingleCommand("A01 UID EXPUNGE 2:3");

         var responseLines = response.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

         Assert.AreEqual(1, responseLines.Length);
         Assert.AreEqual("A01 OK UID EXPUNGE Completed", responseLines[0]);

      }

      [Test]
      [Description("Assert that the EXPUNGE command works")]
      public void TestUidExpungeMessagesFlaggedWithDelete_Range()
      {
         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "ExpungeAccount@test.com",
            "test");

         for (int i = 0; i < 10; i++)
            SmtpClientSimulator.StaticSend("test@test.com", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 10);
         
         var simulator = new ImapClientSimulator();

         Assert.IsTrue(simulator.ConnectAndLogon(account.Address, "test"));
         Assert.IsTrue(simulator.SelectFolder("Inbox"));
         simulator.SetDeletedFlag(1);
         simulator.SetDeletedFlag(2);
         simulator.SetDeletedFlag(4);
         simulator.SetDeletedFlag(7);
         simulator.SetDeletedFlag(9);

         // Message 2 and 4 should be deleted
         var response = simulator.SendSingleCommand("A01 UID EXPUNGE 2:8");

         var responseLines = response.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

         Assert.AreEqual(4, responseLines.Length);
         Assert.AreEqual("* 2 EXPUNGE", responseLines[0]);
         Assert.AreEqual("* 3 EXPUNGE", responseLines[1]);
         Assert.AreEqual("* 5 EXPUNGE", responseLines[2]);
         Assert.AreEqual("A01 OK UID EXPUNGE Completed", responseLines[3]);
      }

      [Test]
      [Description("Assert that the EXPUNGE command works")]
      public void TestUidExpungeMessagesFlaggedWithDelete_RangeAndIndividual()
      {
         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "ExpungeAccount@test.com",
            "test");

         for (int i = 0; i < 10; i++)
            SmtpClientSimulator.StaticSend("test@test.com", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 10);

         var simulator = new ImapClientSimulator();

         Assert.IsTrue(simulator.ConnectAndLogon(account.Address, "test"));
         Assert.IsTrue(simulator.SelectFolder("Inbox"));
         simulator.SetDeletedFlag(1);
         simulator.SetDeletedFlag(2);
         simulator.SetDeletedFlag(4);
         simulator.SetDeletedFlag(7);
         simulator.SetDeletedFlag(9);

         // Message 2 and 4 should be deleted
         var response = simulator.SendSingleCommand("A01 UID EXPUNGE 1:2,7");

         var responseLines = response.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

         Assert.AreEqual(4, responseLines.Length);
         Assert.AreEqual("* 1 EXPUNGE", responseLines[0]);
         Assert.AreEqual("* 1 EXPUNGE", responseLines[1]);
         Assert.AreEqual("* 5 EXPUNGE", responseLines[2]);
         Assert.AreEqual("A01 OK UID EXPUNGE Completed", responseLines[3]);
      }

      [Test]
      [Description("Assert that when one client deletes a message, others are notified - even if IDLE isn't used.")]
      public void TestExpungeNotification()
      {
         _settings.IMAPIdleEnabled = true;

         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "idleaccount@test.com", "test");

         for (int i = 0; i < 5; i++)
            SmtpClientSimulator.StaticSend("test@test.com", account.Address, "Test", "test");

         Pop3ClientSimulator.AssertMessageCount(account.Address, "test", 5);

         var imapClientSimulator = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();
         imapClientSimulator.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         imapClientSimulator.SelectFolder("Inbox");
         simulator2.SelectFolder("Inbox");

         for (int i = 1; i <= 5; i++)
         {
            Assert.IsTrue(imapClientSimulator.SetFlagOnMessage(i, true, @"\Deleted"));
         }

         string noopResponse = simulator2.NOOP() + simulator2.NOOP();

         Assert.IsTrue(noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)") &&
                       noopResponse.Contains(@"* 1 FETCH (FLAGS (\Deleted)"), noopResponse);

         bool result = imapClientSimulator.Expunge();

         string expungeResult = simulator2.NOOP() + simulator2.NOOP();

         Assert.IsTrue(
            expungeResult.Contains("* 1 EXPUNGE\r\n* 1 EXPUNGE\r\n* 1 EXPUNGE\r\n* 1 EXPUNGE\r\n* 1 EXPUNGE"),
            expungeResult);
      }

      [Test]
      public void TestFolderExpungeNotification()
      {
         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "shared@test.com", "test");

         SmtpClientSimulator.StaticSend(account.Address, account.Address, "TestSubject", "TestBody");
         ImapClientSimulator.AssertMessageCount(account.Address, "test", "Inbox", 1);

         var simulator1 = new ImapClientSimulator();
         var simulator2 = new ImapClientSimulator();

         simulator1.ConnectAndLogon(account.Address, "test");
         simulator2.ConnectAndLogon(account.Address, "test");

         simulator1.SelectFolder("Inbox");
         simulator2.SelectFolder("Inbox");

         string result = simulator2.NOOP();
         Assert.IsFalse(result.Contains("Deleted"));
         Assert.IsFalse(result.Contains("Seen"));

         simulator1.SetDeletedFlag(1);
         simulator1.Expunge();

         // the result may (should) come after the first NOOP response stream so do noop twice.
         result = simulator2.NOOP() + simulator2.NOOP();
         Assert.IsTrue(result.Contains("* 1 EXPUNGE"));

         simulator1.Disconnect();
         simulator2.Disconnect();
      }


   }
}