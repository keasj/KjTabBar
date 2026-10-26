using System.Globalization;
using System.Windows;
using KjTabBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class LanguageResourceServiceTests
    {
        [TestMethod]
        public void ImportLimitMessageUsesJapaneseAndEnglishResources()
        {
            System.Exception failure = null;
            System.Threading.Thread worker = new System.Threading.Thread(() =>
            {
                try
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
                    foreach (string culture in new[] { "ja-JP", "en-US", "de-DE" })
                    {
                        System.Collections.Generic.List<ResourceDictionary> dictionaries = new System.Collections.Generic.List<ResourceDictionary>();
                        new LanguageResourceService().ApplyLanguageResource(dictionaries, new CultureInfo(culture));
                        string format = dictionaries[0]["TabListImportLimitExceeded"] as string;
                        Assert.IsFalse(string.IsNullOrEmpty(format));
                        string message = string.Format(format, KjTabBar.ViewModels.TabBarViewModel.MaxTabCount);
                        Assert.AreEqual(culture == "ja-JP" ? "タブ数の上限を超えています。先頭255件をインポートし、超過分はインポートしません。" : "The tab limit has been exceeded. Only the first 255 tabs will be imported.", message);
                    }
                }
                catch (System.Exception ex) { failure = ex; }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(System.Threading.ApartmentState.STA);
            worker.Start();
            Assert.IsTrue(worker.Join(10000));
            if (failure != null) throw failure;
        }

        [TestMethod]
        public void LinkErrorResourcesPreserveJapaneseAndProvideEnglishOnStaWorker()
        {
            System.Exception failure = null;
            System.Threading.Thread worker = new System.Threading.Thread(() =>
            {
                try
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
                    string[] keys = { "ShortcutCreateUnavailable", "ShortcutPlacementFailed", "ShortcutCreateFailed", "SymbolicLinkElevationCancelled", "SymbolicLinkPartialFailure", "LinkOperationErrorTitle", "SymbolicLinkErrorTitle" };
                    string[] japanese = { "ショートカットを作成できませんでした。", "ショートカットの配置に失敗しました。", "ショートカットの作成に失敗しました。", "管理者権限の確認がキャンセルされたため、権限が必要なシンボリックリンクの作成を中止しました。", "一部のシンボリックリンクの作成に失敗しました。", "操作エラー", "エラー" };
                    foreach (string culture in new[] { "ja-JP", "en-US", "de-DE" })
                    {
                        System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                        ResourceDictionary resources = KjTabBar.Models.ShellShortcutManager.LoadOperationErrorResources();
                        for (int i = 0; i < keys.Length; i++)
                        {
                            string message = resources[keys[i]] as string;
                            Assert.IsFalse(string.IsNullOrWhiteSpace(message), culture + ":" + keys[i]);
                            if (culture == "ja-JP") Assert.AreEqual(japanese[i], message);
                            else Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(message, @"[^\u0000-\u007f]"), keys[i]);
                        }
                    }
                }
                catch (System.Exception ex) { failure = ex; }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(System.Threading.ApartmentState.STA);
            worker.Start();
            Assert.IsTrue(worker.Join(10000));
            if (failure != null) throw failure;
        }
        [TestMethod]
        public void GetDictionaryName_UsesJapaneseDictionary_ForJaCulture()
        {
            LanguageResourceService service = new LanguageResourceService();

            string result = service.GetDictionaryName(new CultureInfo("ja-JP"));

            Assert.AreEqual("StringResources.ja.xaml", result);
        }
    }
}
