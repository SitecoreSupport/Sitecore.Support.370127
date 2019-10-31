using System;
using System.Collections.Concurrent;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.StringExtensions;
using Sitecore.XA.Foundation.IoC;
using Sitecore.XA.Foundation.Multisite;
using Sitecore.XA.Foundation.TokenResolution.Pipelines.ResolveTokens;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Data.Events;
using Sitecore.Diagnostics;
using Sitecore.Events;

namespace Sitecore.Support.XA.Foundation.Multisite.Pipelines.ResolveTokens
{
    public class ResolveMultisiteTokens : ResolveTokensProcessor
    {
        private static readonly ConcurrentDictionary<string, string> TenantTemplateRootDictionary = new ConcurrentDictionary<string, string>();

        protected IMultisiteContext MultisiteContext
        {
            get;
            set;
        }

        public ResolveMultisiteTokens()
        {
            MultisiteContext = ServiceLocator.Current.Resolve<IMultisiteContext>();
        }

        public override void Process(ResolveTokensArgs args)
        {
            string query = args.Query;
            Item contextItem = args.ContextItem;
            bool escapeSpaces = args.EscapeSpaces;
            query = ReplaceTokenWithItemPath(query, "$tenant", () => MultisiteContext.GetTenantItem(contextItem), escapeSpaces);
            query = ReplaceTokenWithItemPath(query, "$siteMedia", () => MultisiteContext.GetSiteMediaItem(contextItem), escapeSpaces);
            query = ReplaceTokenWithItemPath(query, "$site", () => MultisiteContext.GetSiteItem(contextItem), escapeSpaces);
            query = ReplaceTokenWithItemPath(query, "$home", () => ServiceLocator.Current.Resolve<ISiteInfoResolver>().GetStartPath(contextItem), escapeSpaces);
            query = (args.Query = ReplaceTokenWithValue(query, "$templates", () => GetTenantTemplatesQuery(contextItem)));
        }

        protected virtual string GetTenantTemplatesQuery(Item contextItem)
        {
            var contextDatabase = contextItem.Database;

            string arg = "/sitecore/templates";
            string arg2 = "[@@templatename='Template']";

            Item tenantTemplatesRoot = GetTenantTemplatesRoot(contextItem);
            if (tenantTemplatesRoot != null)
            {
                if (!TenantTemplateRootDictionary.ContainsKey(tenantTemplatesRoot.Paths.FullPath))
                {
                    arg = tenantTemplatesRoot.Paths.Path;
                    IEnumerable<Item> source = from item in tenantTemplatesRoot.Axes.GetDescendants()
                                               where item.TemplateName.Equals("Template")
                                               where TemplateManager.GetTemplate(item.ID, item.Database).DescendsFrom(Templates.Page.ID)
                                               select item;
                    string text = string.Join(" or ", from item in source
                                                      select $"@@id='{item.ID}'");
                    if (!text.IsNullOrEmpty())
                    {
                        arg2 = $"[{text}]";
                    }
                    TenantTemplateRootDictionary[tenantTemplatesRoot.Paths.FullPath] = $"{arg}//*{arg2}";
                    return $"{arg}//*{arg2}";
                }
                return TenantTemplateRootDictionary[tenantTemplatesRoot.Paths.FullPath];
            }
            return $"{arg}//*{arg2}";
        }

        protected virtual Item GetTenantTemplatesRoot(Item contextItem)
        {
            Item settingsItem = MultisiteContext.GetSettingsItem(contextItem);
            if (settingsItem != null)
            {
                string text = settingsItem[Templates.Settings.Fields.Templates];
                ID result;
                if (ID.TryParse(text, out result))
                {
                    return contextItem.Database.GetItem(text);
                }
            }
            return null;
        }

        [UsedImplicitly]
        private void OnItemCreated(object sender, EventArgs args)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(args, "args");
            string added =((ItemCreatedEventArgs)((SitecoreEventArgs) args).Parameters[0])?.Item?.Paths?.FullPath;
            if (!string.IsNullOrEmpty(added))
            {
                for (int i = 0; i < TenantTemplateRootDictionary.Keys.Count; i++)
                {
                    if (added.Contains(TenantTemplateRootDictionary.Keys.ElementAt(i)))
                    {
                        string result;
                        TenantTemplateRootDictionary.TryRemove(TenantTemplateRootDictionary.Keys.ElementAt(i), out result);
                    }
                }
            }
        }

        [UsedImplicitly]
        private void OnItemDeleting(object sender, EventArgs args)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(args, "args");
            Item deletingItem = Event.ExtractParameter(args, 0) as Item;
            string deletingItemPath = deletingItem?.Paths?.FullPath;
            if (!string.IsNullOrEmpty(deletingItemPath))
            {
                for (int i = 0; i < TenantTemplateRootDictionary.Keys.Count; i++)
                {
                    if (deletingItemPath.Contains(TenantTemplateRootDictionary.Keys.ElementAt(i)))
                    {
                        string result;
                        TenantTemplateRootDictionary.TryRemove(TenantTemplateRootDictionary.Keys.ElementAt(i), out result);
                    }
                }
            }
        }
    }
}
