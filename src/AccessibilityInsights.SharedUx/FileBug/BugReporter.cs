﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Extensions.Interfaces.IssueReporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AccessibilityInsights.SharedUx.FileBug
{
    /// <summary>
    /// Adapter between the core app and the bug reporting extension
    /// </summary>
    static public class BugReporter
    {
        public static IIssueReporting IssueReporting { get; set; }

        public static bool IsEnabled => (IssueReporterManager.GetInstance().GetIssueFilingOptionsDict() != null && IssueReporterManager.GetInstance().GetIssueFilingOptionsDict().Any());

        public static bool IsConnected => IsEnabled && (IssueReporting == null ? false : IssueReporting.IsConfigured);

#pragma warning disable CA1819 // Properties should not return arrays
        public static byte[] Logo => IsEnabled ? IssueReporting.Logo?.ToArray() : null;
#pragma warning restore CA1819 // Properties should not return arrays

        public static string DisplayName => IsEnabled ? IssueReporting.ServiceName : null;

        public static Dictionary<Guid, IIssueReporting> GetIssueReporters() {
            return IssueReporterManager.GetInstance().GetIssueFilingOptionsDict();
        }

        public static Task RestoreConfigurationAsync(string serializedConfig)
        {
            if (IsEnabled && IssueReporterManager.SelectedIssueReporterGuid != null)
                return IssueReporting.RestoreConfigurationAsync(serializedConfig);
            return Task.CompletedTask;
        }

        public static IIssueResult FileIssueAsync(IssueInformation issueInformation)
        {
            if (IsEnabled && IsConnected) {
                // Coding to the agreement that FileIssueAsync will return a kicked off task. 
                // This will block the main thread. 
                // It does seem like we currently block the main thread when we show the win form for azure devops
                // so keeping it as is till we have a discussion. Check for blocking behavior at that link.
                // https://github.com/Microsoft/accessibility-insights-windows/blob/master/src/AccessibilityInsights.SharedUx/Controls/HierarchyControl.xaml.cs#L858
                return IssueReporting.FileIssueAsync(issueInformation).Result;
            }
            return null;
        }
    }
}
