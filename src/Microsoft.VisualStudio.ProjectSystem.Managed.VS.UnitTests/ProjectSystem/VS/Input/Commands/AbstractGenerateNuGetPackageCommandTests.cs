﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell.Interop;
using Xunit;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Input.Commands
{
    public abstract class AbstractGenerateNuGetPackageCommandTests
    {
        [Fact]
        public void Constructor_NullAsUnconfiguredProject_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => CreateInstanceCore(null, IProjectThreadingServiceFactory.Create(), SVsServiceProviderFactory.Create(null), CreateGeneratePackageOnBuildPropertyProvider()));
        }

        [Fact]
        public void Constructor_NullAsProjectThreadingServiceFactory_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => CreateInstanceCore(UnconfiguredProjectFactory.Create(), null, SVsServiceProviderFactory.Create(null), CreateGeneratePackageOnBuildPropertyProvider()));
        }

        [Fact]
        public void Constructor_NullAsSVsServiceProvider_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => CreateInstanceCore(UnconfiguredProjectFactory.Create(), IProjectThreadingServiceFactory.Create(), null, CreateGeneratePackageOnBuildPropertyProvider()));
        }

        [Fact]
        public void Constructor_NullAsGeneratePackageOnBuildPropertyProvider_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => CreateInstanceCore(UnconfiguredProjectFactory.Create(), IProjectThreadingServiceFactory.Create(), SVsServiceProviderFactory.Create(null), null));
        }

        [Fact]
        public async Task TryHandleCommandAsync_InvokesBuild()
        {
            bool buildStarted = false, buildCancelled = false, buildCompleted = false;
            Action onUpdateSolutionBegin = () => { buildStarted = true; };
            Action onUpdateSolutionCancel = () => { buildCancelled = true; };
            Action onUpdateSolutionDone = () => { buildCompleted = true; };

            var solutionEventsListener = IVsUpdateSolutionEventsFactory.Create(onUpdateSolutionBegin, onUpdateSolutionCancel, onUpdateSolutionDone);
            var command = CreateInstance(solutionEventsListener: solutionEventsListener);

            var tree = ProjectTreeParser.Parse(@"
Root (flags: {ProjectRoot})
");

            var nodes = ImmutableHashSet.Create(tree.Root);

            var result = await command.TryHandleCommandAsync(nodes, GetCommandId(), true, 0, IntPtr.Zero, IntPtr.Zero);

            Assert.True(result);
            Assert.True(buildStarted);
            Assert.True(buildCompleted);
            Assert.False(buildCancelled);
        }

        [Fact]
        public async Task TryHandleCommandAsync_OnBuildCancelled()
        {
            bool buildStarted = false, buildCancelled = false, buildCompleted = false;
            Action onUpdateSolutionBegin = () => { buildStarted = true; };
            Action onUpdateSolutionCancel = () => { buildCancelled = true; };
            Action onUpdateSolutionDone = () => { buildCompleted = true; };

            var solutionEventsListener = IVsUpdateSolutionEventsFactory.Create(onUpdateSolutionBegin, onUpdateSolutionCancel, onUpdateSolutionDone);
            var command = CreateInstance(solutionEventsListener: solutionEventsListener, cancelBuild: true);

            var tree = ProjectTreeParser.Parse(@"
Root (flags: {ProjectRoot})
");

            var nodes = ImmutableHashSet.Create(tree.Root);

            var result = await command.TryHandleCommandAsync(nodes, GetCommandId(), true, 0, IntPtr.Zero, IntPtr.Zero);

            Assert.True(result);
            Assert.True(buildStarted);
            Assert.False(buildCompleted);
            Assert.True(buildCancelled);
        }

        [Fact]
        public async Task GetCommandStatusAsync_BuildInProgress()
        {
            var tree = ProjectTreeParser.Parse(@"
Root (flags: {ProjectRoot})
");

            var nodes = ImmutableHashSet.Create(tree.Root);

            // Command is enabled if there is no build in progress.
            var command = CreateInstance(isBuilding: false);
            var results = await command.GetCommandStatusAsync(nodes, GetCommandId(), true, "commandText", (CommandStatus)0);
            Assert.True(results.Handled);
            Assert.Equal(CommandStatus.Enabled | CommandStatus.Supported, results.Status);

            // Command is disabled if there is build in progress.
            command = CreateInstance(isBuilding: true);
            results = await command.GetCommandStatusAsync(nodes, GetCommandId(), true, "commandText", (CommandStatus)0);
            Assert.True(results.Handled);
            Assert.Equal(CommandStatus.Supported, results.Status);
        }

        [Fact]
        public async Task TryHandleCommandAsync_BuildInProgress()
        {
            var tree = ProjectTreeParser.Parse(@"
Root (flags: {ProjectRoot})
");

            var nodes = ImmutableHashSet.Create(tree.Root);

            bool buildStarted = false, buildCancelled = false, buildCompleted = false;
            Action onUpdateSolutionBegin = () => { buildStarted = true; };
            Action onUpdateSolutionCancel = () => { buildCancelled = true; };
            Action onUpdateSolutionDone = () => { buildCompleted = true; };
            var solutionEventsListener = IVsUpdateSolutionEventsFactory.Create(onUpdateSolutionBegin, onUpdateSolutionCancel, onUpdateSolutionDone);

            var command = CreateInstance(solutionEventsListener: solutionEventsListener, isBuilding: true);

            // Ensure we handle the command, but don't invoke build as there is a build already in progress.
            var handled = await command.TryHandleCommandAsync(nodes, GetCommandId(), true, 0, IntPtr.Zero, IntPtr.Zero);
            Assert.True(handled);
            Assert.False(buildStarted);
            Assert.False(buildCompleted);
            Assert.False(buildCancelled);
        }

        internal abstract long GetCommandId();

        internal AbstractGenerateNuGetPackageCommand CreateInstance(
            GeneratePackageOnBuildPropertyProvider generatePackageOnBuildPropertyProvider = null,
            IVsSolutionBuildManager2 buildManager = null,
            IVsUpdateSolutionEvents solutionEventsListener = null,
            bool isBuilding = false,
            bool cancelBuild = false)
        {
            var hierarchy = IVsHierarchyFactory.Create();
            var unconfiguredProject = UnconfiguredProjectFactory.Create(hierarchy);
            var threadingService = IProjectThreadingServiceFactory.Create();
            buildManager = buildManager ?? IVsSolutionBuildManager2Factory.Create(solutionEventsListener, hierarchy, isBuilding, cancelBuild);
            var serviceProvider = SVsServiceProviderFactory.Create(buildManager);
            generatePackageOnBuildPropertyProvider = generatePackageOnBuildPropertyProvider ?? CreateGeneratePackageOnBuildPropertyProvider();

            return CreateInstanceCore(unconfiguredProject, threadingService, serviceProvider, generatePackageOnBuildPropertyProvider);
        }

        private GeneratePackageOnBuildPropertyProvider CreateGeneratePackageOnBuildPropertyProvider(IProjectService projectService = null)
        {
            projectService = projectService ?? IProjectServiceFactory.Create();
            return new GeneratePackageOnBuildPropertyProvider(projectService);
        }

        internal abstract AbstractGenerateNuGetPackageCommand CreateInstanceCore(
            UnconfiguredProject unconfiguredProject,
            IProjectThreadingService threadingService,
            Shell.SVsServiceProvider serviceProvider,
            GeneratePackageOnBuildPropertyProvider generatePackageOnBuildPropertyProvider);
    }
}
