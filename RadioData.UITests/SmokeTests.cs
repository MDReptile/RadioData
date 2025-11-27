using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Window = FlaUI.Core.AutomationElements.Window;

namespace RadioData.UITests
{
    [TestFixture]
    public class SmokeTests
    {
        private TextBox? _testLogInput;

        private void LogToApp(string message)
        {
            if (_testLogInput != null)
            {
                _testLogInput.Text = message;
                Thread.Sleep(300);
            }
            Console.WriteLine($"[TEST] {message}");
        }

        [Test]
        public void ComprehensiveUITest_AllTextTransmissions()
        {
            Process process = null;
            UIA3Automation automation = null;

            try
            {
                Console.WriteLine("\n========================================");
                Console.WriteLine("COMPREHENSIVE TEXT TEST");
                Console.WriteLine("========================================\n");

                string solutionDir = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../.."));
                string appPath = Path.Combine(solutionDir, "RadioDataApp", "bin", "Debug", "net8.0-windows", "RadioDataApp.exe");

                if (!File.Exists(appPath)) Assert.Fail("Application not found");

                Console.WriteLine("[LAUNCH] Starting app...");
                process = Process.Start(new ProcessStartInfo { FileName = appPath, UseShellExecute = true });
                Assert.That(process, Is.Not.Null);
                Thread.Sleep(4000);

                automation = new UIA3Automation();
                Window window = null;
                for (int i = 0; i < 10; i++)
                {
                    var desktop = automation.GetDesktop();
                    foreach (var child in desktop.FindAllChildren())
                    {
                        try
                        {
                            var win = child.AsWindow();
                            if (win?.Title?.Contains("RadioData") == true && !win.Title.Contains("Visual Studio"))
                            {
                                window = win;
                                break;
                            }
                        }
                        catch { }
                    }
                    if (window != null) break;
                    Thread.Sleep(500);
                }

                Assert.That(window, Is.Not.Null);
                Thread.Sleep(1500);

                var messageBox = window.FindFirstDescendant(cf => cf.ByAutomationId("MessageTextBox"))?.AsTextBox();
                var sendTextButton = window.FindFirstDescendant(cf => cf.ByAutomationId("SendTextButton"))?.AsButton();
                var chatBox = window.FindFirstDescendant(cf => cf.ByAutomationId("ChatTextBox"))?.AsTextBox();
                _testLogInput = window.FindFirstDescendant(cf => cf.ByAutomationId("TestLogInput"))?.AsTextBox();
                var inputDeviceCombo = window.FindFirstDescendant(cf => cf.ByAutomationId("InputDeviceCombo"))?.AsComboBox();
                var outputDeviceCombo = window.FindFirstDescendant(cf => cf.ByAutomationId("OutputDeviceCombo"))?.AsComboBox();
                var inputGainSlider = window.FindFirstDescendant(cf => cf.ByAutomationId("InputGainSlider"))?.AsSlider();

                Assert.That(messageBox, Is.Not.Null);
                Assert.That(sendTextButton, Is.Not.Null);
                Assert.That(chatBox, Is.Not.Null);
                Assert.That(_testLogInput, Is.Not.Null);
                Assert.That(inputDeviceCombo, Is.Not.Null);
                Assert.That(outputDeviceCombo, Is.Not.Null);
                Assert.That(inputGainSlider, Is.Not.Null);

                Thread.Sleep(500);

                LogToApp("Starting test");

                var originalInputItem = inputDeviceCombo.SelectedItem;
                var originalOutputItem = outputDeviceCombo.SelectedItem;

                inputDeviceCombo.Select(0);
                Thread.Sleep(800);
                outputDeviceCombo.Select(0);
                Thread.Sleep(800);
                inputGainSlider.Value = 0.6;
                Thread.Sleep(500);

                string[] testMessages = new[]
                {
                    "ABCabc123 !@#$%^&*()_+-=[]{}|;':\"<>,.?/~`\\",
                    "The quick brown fox jumps over the lazy dog 0123456789 !@#$%"
                };

                int passed = 0;
                int failed = 0;

                for (int i = 0; i < testMessages.Length; i++)
                {
                    string msg = testMessages[i];
                    Console.WriteLine($"[{i + 1}/{testMessages.Length}] {msg}");

                    messageBox.Text = msg;
                    Thread.Sleep(200);
                    sendTextButton.Click();
                    Thread.Sleep(200);

                    bool done = false;
                    for (int j = 0; j < 30; j++)
                    {
                        Thread.Sleep(500);
                        try
                        {
                            if (sendTextButton.IsEnabled)
                            {
                                done = true;
                                break;
                            }
                        }
                        catch (System.Runtime.InteropServices.COMException)
                        {
                            Thread.Sleep(300);
                        }
                    }

                    if (!done)
                    {
                        Console.WriteLine($"[{i + 1}] TIMEOUT");
                        failed++;
                        continue;
                    }

                    Thread.Sleep(300);

                    var chat = chatBox.Text ?? "";
                    bool tx = chat.Contains($">> [") && chat.Contains($"] {msg}");
                    bool rx = chat.Contains($"<< [") && chat.Contains($"] {msg}");
                    
                    if (tx && rx)
                    {
                        Console.WriteLine($"[{i + 1}] PASS");
                        passed++;
                    }
                    else
                    {
                        Console.WriteLine($"[{i + 1}] FAIL (tx={tx} rx={rx})");
                        failed++;
                    }
                }

                if (originalInputItem != null) inputDeviceCombo.Select(originalInputItem.Text);
                Thread.Sleep(200);
                if (originalOutputItem != null) outputDeviceCombo.Select(originalOutputItem.Text);
                Thread.Sleep(200);

                Console.WriteLine($"\n========================================");
                Console.WriteLine($"RESULTS: {passed}/{testMessages.Length} passed");
                Console.WriteLine($"========================================\n");

                Assert.That(failed, Is.EqualTo(0), $"{failed} tests failed");

                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR] {ex.Message}");
                Thread.Sleep(500);
                throw;
            }
            finally
            {
                automation?.Dispose();
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
        }

        [Test]
        public void ComprehensiveUITest_FileSending()
        {
            Assert.Inconclusive("File dialog automation not supported.");
        }
    }
}
