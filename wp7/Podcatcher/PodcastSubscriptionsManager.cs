﻿/**
 * Copyright (c) 2012, Johan Paul <johan@paul.fi>
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the <organization> nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Net;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows.Navigation;
using System.Windows;
using Microsoft.Phone.Controls;
using System.Windows.Controls;
using Coding4Fun.Phone.Controls;
using Podcatcher.ViewModels;
using System.Text;
using System.ComponentModel;
using System.Collections.Generic;

namespace Podcatcher
{
    public delegate void SubscriptionManagerHandler(object source, SubscriptionManagerArgs e);

    public class SubscriptionManagerArgs
    {
        public String message;
        public PodcastSubscriptionModel addedSubscription;
        public PodcastSubscriptionsManager.SubscriptionsState state;
    }

    public class PodcastSubscriptionsManager
    {
        /************************************* Public implementations *******************************/

        public event SubscriptionManagerHandler OnPodcastChannelStarted;
        public event SubscriptionManagerHandler OnPodcastChannelFinished;
        public event SubscriptionManagerHandler OnPodcastChannelFinishedWithError;
        public event SubscriptionManagerHandler OnPodcastSubscriptionsChanged;

        public enum SubscriptionsState
        {
            StartedRefreshing,
            FinishedRefreshing
        }

        public static PodcastSubscriptionsManager getInstance()
        {
            if (m_subscriptionManagerInstance == null)
            {
                m_subscriptionManagerInstance = new PodcastSubscriptionsManager();
            }

            return m_subscriptionManagerInstance;
        }

        public void addSubscriptionFromURL(string podcastRss)
        {

            if (String.IsNullOrEmpty(podcastRss))
            {
#if DEBUG
                podcastRss = "http://leo.am/podcasts/twit";
#else
                Debug.WriteLine("ERROR: Empty URL.");
                PodcastSubscriptionFailedWithMessage("Empty podcast address.");
                return; 
#endif
            }

            if (podcastRss.StartsWith("http://") == false)
            {
                podcastRss = podcastRss.Insert(0, "http://");
            }

            Uri podcastRssUri;
            try
            {
                podcastRssUri = new Uri(podcastRss);
            }
            catch (UriFormatException)
            {
                Debug.WriteLine("ERROR: Cannot add podcast from that URL.");
                OnPodcastChannelFinishedWithError(this, null);
                return;
            }

            WebClient wc = new WebClient();
            wc.DownloadStringCompleted += new DownloadStringCompletedEventHandler(wc_DownloadPodcastRSSCompleted);
            wc.DownloadStringAsync(podcastRssUri, podcastRss);

            OnPodcastChannelStarted(this, null);

            Debug.WriteLine("Fetching podcast from URL: " + podcastRss.ToString());
        }

        public void deleteSubscription(PodcastSubscriptionModel podcastSubscriptionModel)
        {
            podcastSubscriptionModel.cleanupForDeletion();
            m_podcastsSqlModel.deleteSubscription(podcastSubscriptionModel);
        }

        public void refreshSubscriptions()
        {
            stateChangedArgs.state = PodcastSubscriptionsManager.SubscriptionsState.StartedRefreshing;
            OnPodcastSubscriptionsChanged(this, stateChangedArgs);

            m_subscriptions = m_podcastsSqlModel.PodcastSubscriptions;
            refreshNextSubscription();
        }

        /************************************* Private implementation *******************************/
        #region privateImplementations
        private static PodcastSubscriptionsManager m_subscriptionManagerInstance = null;
        private PodcastSqlModel m_podcastsSqlModel            = null;
        private Random m_random                               = null;
        private SubscriptionManagerArgs stateChangedArgs      = new SubscriptionManagerArgs();
        List<PodcastSubscriptionModel> m_subscriptions        = null;

        private PodcastSubscriptionsManager()
        {
            m_podcastsSqlModel = PodcastSqlModel.getInstance();
            m_random = new Random();

            // Hook a callback method to the signal that we emit when the subscription has been added.
            // This way we can continue the synchronous execution.
            this.OnPodcastChannelFinished += new SubscriptionManagerHandler(PodcastSubscriptionsManager_OnPodcastAddedFinished);
        }


        private Uri createNonCachedRefreshUri(string refreshUri)
        {
            string delimitter = "&";
            if (refreshUri.Contains("?") == false)
            {
                delimitter = "?";
            }

            return new Uri(string.Format("{0}{1}nocache={2}", refreshUri,
                                                              delimitter,
                                                              Environment.TickCount));

        }

        private void wc_DownloadPodcastRSSCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            if (e.Error != null
                || e.Cancelled)
            {
                PodcastSubscriptionFailedWithMessage("Error retrieving podcast feed.");
                return;
            }

            string podcastRss = e.Result;
            PodcastSubscriptionModel podcastModel = PodcastFactory.podcastModelFromRSS(podcastRss);
            if (podcastModel == null)
            {
                PodcastSubscriptionFailedWithMessage("Could not parse podcast subscription.");
                return;
            }

            string rssUrl = e.UserState as string;
            if (m_podcastsSqlModel.isPodcastInDB(rssUrl))
            {
                PodcastSubscriptionFailedWithMessage("You have already subscribed to that podcast.");
                return;
            }

            podcastModel.CachedPodcastRSSFeed = podcastRss;                        
            podcastModel.PodcastLogoLocalLocation = localLogoFileName(podcastModel);
            podcastModel.PodcastRSSUrl = rssUrl;
            m_podcastsSqlModel.addSubscription(podcastModel);

            podcastModel.fetchChannelLogo();

            SubscriptionManagerArgs addArgs = new SubscriptionManagerArgs();
            addArgs.addedSubscription = podcastModel;

            OnPodcastChannelFinished(this, addArgs);
        }

        private void refreshNextSubscription()
        {
            if (m_subscriptions.Count < 1)
            {
                stateChangedArgs.state = PodcastSubscriptionsManager.SubscriptionsState.FinishedRefreshing;
                OnPodcastSubscriptionsChanged(this, stateChangedArgs);

                return;
            }

            PodcastSubscriptionModel subscription = m_subscriptions[0];
            m_subscriptions.RemoveAt(0);

            if (subscription.IsSubscribed == false)
            {
                Debug.WriteLine("Not subscribed to {0}, no refresh.", subscription.PodcastName);
                refreshNextSubscription();
            }

            Uri refreshUri = createNonCachedRefreshUri(subscription.PodcastRSSUrl);
            Debug.WriteLine("Refreshing subscriptions for '{0}', using URL: {1}", subscription.PodcastName, refreshUri);

            WebClient wc = new WebClient();
            wc.DownloadStringCompleted += new DownloadStringCompletedEventHandler(wc_RefreshPodcastRSSCompleted);
            wc.DownloadStringAsync(refreshUri, subscription);
        }

        void wc_RefreshPodcastRSSCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Debug.WriteLine("ERROR: Got web error when refreshing subscriptions: " + e.ToString());
                ToastPrompt toast = new ToastPrompt();
                toast.Title = "Error";
                toast.Message = "Cannot refresh subscriptions.";

                toast.Show();
                return;
            }

            PodcastSubscriptionModel subscription = e.UserState as PodcastSubscriptionModel;
            subscription.CachedPodcastRSSFeed = e.Result as string;

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler(workerUpdateEpisodes);
            worker.RunWorkerAsync(subscription);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(workerUpdateEpisodesCompleted);
        }

        private void workerUpdateEpisodes(object sender, DoWorkEventArgs args)
        {
            PodcastSubscriptionModel subscription = args.Argument as PodcastSubscriptionModel;
            Debug.WriteLine("Starting refreshing episodes for " + subscription.PodcastName);
            lock (subscription)
            {
                subscription.EpisodesManager.updatePodcastEpisodes();
            }
            Debug.WriteLine("Done.");
        }

        private void workerUpdateEpisodesCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            refreshNextSubscription();
        }

        private void PodcastSubscriptionsManager_OnPodcastAddedFinished(object source, SubscriptionManagerArgs e)
        {
            PodcastSubscriptionModel subscriptionModel = e.addedSubscription;
            Debug.WriteLine("Podcast added successfully. Name: " + subscriptionModel.PodcastName);

            subscriptionModel.EpisodesManager.updatePodcastEpisodes();
        }

        private void PodcastSubscriptionFailedWithMessage(string message)
        {
            Debug.WriteLine(message);
            SubscriptionManagerArgs args = new SubscriptionManagerArgs();
            args.message = message;

            OnPodcastChannelFinishedWithError(this, args);
        }

        private string localLogoFileName(PodcastSubscriptionModel podcastModel)
        {
            string podcastLogoFilename;
            if (String.IsNullOrEmpty(podcastModel.PodcastLogoUrl.ToString()))
            {
                // Podcast logo URL is empty - use default placeholder logo.
                podcastLogoFilename = @"Podcatcher_generic_podcast_cover.png";
            }
            else
            {
                // Parse the filename of the logo from the remote URL.
                string localPath = podcastModel.PodcastLogoUrl.LocalPath;
                podcastLogoFilename = localPath.Substring(localPath.LastIndexOf('/') + 1);
            }

            // Make podcast logo name random.
            // This is because, if for some reason, two podcasts have the same logo name and we delete
            // one of them, we don't want the other one to be affected. Just to be sure. 
            StringBuilder podcastLogoFilename_sb = new StringBuilder(podcastLogoFilename);
            podcastLogoFilename_sb.Insert(0, m_random.Next().ToString());

            string localPodcastLogoFilename = App.PODCAST_ICON_DIR + @"/" + podcastLogoFilename_sb.ToString();
            Debug.WriteLine("Found icon filename: " + localPodcastLogoFilename);

            return localPodcastLogoFilename;
        }
        #endregion
    }
}
