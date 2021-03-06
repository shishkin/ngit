/*
This code is derived from jgit (http://eclipse.org/jgit).
Copyright owners are documented in jgit's IP log.

This program and the accompanying materials are made available
under the terms of the Eclipse Distribution License v1.0 which
accompanies this distribution, is reproduced below, and is
available at http://www.eclipse.org/org/documents/edl-v10.php

All rights reserved.

Redistribution and use in source and binary forms, with or
without modification, are permitted provided that the following
conditions are met:

- Redistributions of source code must retain the above copyright
  notice, this list of conditions and the following disclaimer.

- Redistributions in binary form must reproduce the above
  copyright notice, this list of conditions and the following
  disclaimer in the documentation and/or other materials provided
  with the distribution.

- Neither the name of the Eclipse Foundation, Inc. nor the
  names of its contributors may be used to endorse or promote
  products derived from this software without specific prior
  written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System.Collections.Generic;
using System.IO;
using NGit;
using NGit.Api;
using NGit.Api.Errors;
using NGit.Dircache;
using NGit.Revwalk;
using NGit.Transport;
using Sharpen;

namespace NGit.Api
{
	/// <summary>Clone a repository into a new working directory</summary>
	/// <seealso><a href="http://www.kernel.org/pub/software/scm/git/docs/git-clone.html"
	/// *      >Git documentation about Clone</a></seealso>
	public class CloneCommand : Callable<Git>
	{
		private string uri;

		private FilePath directory;

		private bool bare;

		private string remote = Constants.DEFAULT_REMOTE_NAME;

		private string branch = Constants.HEAD;

		private ProgressMonitor monitor = NullProgressMonitor.INSTANCE;

		private CredentialsProvider credentialsProvider;

		private int timeout;

		private bool cloneAllBranches;

		private ICollection<string> branchesToClone;

		/// <summary>
		/// Executes the
		/// <code>Clone</code>
		/// command.
		/// </summary>
		/// <exception cref="NGit.Api.Errors.JGitInternalException">if the repository can't be created
		/// 	</exception>
		/// <returns>
		/// the newly created
		/// <code>Git</code>
		/// object with associated repository
		/// </returns>
		public virtual Git Call()
		{
			try
			{
				URIish u = new URIish(uri);
				Repository repository = Init(u);
				FetchResult result = Fetch(repository, u);
				Checkout(repository, result);
				return new Git(repository);
			}
			catch (IOException ioe)
			{
				throw new JGitInternalException(ioe.Message, ioe);
			}
			catch (InvalidRemoteException e)
			{
				throw new JGitInternalException(e.Message, e);
			}
			catch (URISyntaxException e)
			{
				throw new JGitInternalException(e.Message, e);
			}
		}

		private Repository Init(URIish u)
		{
			InitCommand command = Git.Init();
			command.SetBare(bare);
			if (directory == null)
			{
				directory = new FilePath(u.GetHumanishName(), Constants.DOT_GIT);
			}
			command.SetDirectory(directory);
			return command.Call().GetRepository();
		}

		/// <exception cref="Sharpen.URISyntaxException"></exception>
		/// <exception cref="NGit.Api.Errors.JGitInternalException"></exception>
		/// <exception cref="NGit.Api.Errors.InvalidRemoteException"></exception>
		/// <exception cref="System.IO.IOException"></exception>
		private FetchResult Fetch(Repository repo, URIish u)
		{
			// create the remote config and save it
			RemoteConfig config = new RemoteConfig(repo.GetConfig(), remote);
			config.AddURI(u);
			string dst = Constants.R_REMOTES + config.Name;
			RefSpec refSpec = new RefSpec();
			refSpec = refSpec.SetForceUpdate(true);
			refSpec = refSpec.SetSourceDestination(Constants.R_HEADS + "*", dst + "/*");
			//$NON-NLS-1$ //$NON-NLS-2$
			config.AddFetchRefSpec(refSpec);
			config.Update(repo.GetConfig());
			repo.GetConfig().Save();
			// run the fetch command
			FetchCommand command = new FetchCommand(repo);
			command.SetRemote(remote);
			command.SetProgressMonitor(monitor);
			command.SetTagOpt(TagOpt.FETCH_TAGS);
			command.SetTimeout(timeout);
			if (credentialsProvider != null)
			{
				command.SetCredentialsProvider(credentialsProvider);
			}
			IList<RefSpec> specs = CalculateRefSpecs(dst);
			command.SetRefSpecs(specs);
			return command.Call();
		}

		private IList<RefSpec> CalculateRefSpecs(string dst)
		{
			RefSpec wcrs = new RefSpec();
			wcrs = wcrs.SetForceUpdate(true);
			wcrs = wcrs.SetSourceDestination(Constants.R_HEADS + "*", dst + "/*");
			//$NON-NLS-1$ //$NON-NLS-2$
			IList<RefSpec> specs = new AList<RefSpec>();
			if (cloneAllBranches)
			{
				specs.AddItem(wcrs);
			}
			else
			{
				if (branchesToClone != null && branchesToClone.Count > 0)
				{
					foreach (string selectedRef in branchesToClone)
					{
						if (wcrs.MatchSource(selectedRef))
						{
							specs.AddItem(wcrs.ExpandFromSource(selectedRef));
						}
					}
				}
			}
			return specs;
		}

		/// <exception cref="NGit.Api.Errors.JGitInternalException"></exception>
		/// <exception cref="NGit.Errors.MissingObjectException"></exception>
		/// <exception cref="NGit.Errors.IncorrectObjectTypeException"></exception>
		/// <exception cref="System.IO.IOException"></exception>
		private void Checkout(Repository repo, FetchResult result)
		{
			Ref head = result.GetAdvertisedRef(branch);
			if (branch.Equals(Constants.HEAD))
			{
				Ref foundBranch = FindBranchToCheckout(result);
				if (foundBranch != null)
				{
					head = foundBranch;
				}
			}
			if (head == null || head.GetObjectId() == null)
			{
				return;
			}
			// throw exception?
			if (head.GetName().StartsWith(Constants.R_HEADS))
			{
				RefUpdate newHead = repo.UpdateRef(Constants.HEAD);
				newHead.DisableRefLog();
				newHead.Link(head.GetName());
				AddMergeConfig(repo, head);
			}
			RevCommit commit = ParseCommit(repo, head);
			bool detached = !head.GetName().StartsWith(Constants.R_HEADS);
			RefUpdate u = repo.UpdateRef(Constants.HEAD, detached);
			u.SetNewObjectId(commit.Id);
			u.ForceUpdate();
			if (!bare)
			{
				DirCache dc = repo.LockDirCache();
				DirCacheCheckout co = new DirCacheCheckout(repo, dc, commit.Tree);
				co.Checkout();
			}
		}

		private Ref FindBranchToCheckout(FetchResult result)
		{
			Ref foundBranch = null;
			Ref idHEAD = result.GetAdvertisedRef(Constants.HEAD);
			foreach (Ref r in result.GetAdvertisedRefs())
			{
				string n = r.GetName();
				if (!n.StartsWith(Constants.R_HEADS))
				{
					continue;
				}
				if (idHEAD == null)
				{
					continue;
				}
				if (r.GetObjectId().Equals(idHEAD.GetObjectId()))
				{
					foundBranch = r;
					break;
				}
			}
			return foundBranch;
		}

		/// <exception cref="System.IO.IOException"></exception>
		private void AddMergeConfig(Repository repo, Ref head)
		{
			string branchName = Repository.ShortenRefName(head.GetName());
			repo.GetConfig().SetString(ConfigConstants.CONFIG_BRANCH_SECTION, branchName, ConfigConstants
				.CONFIG_KEY_REMOTE, remote);
			repo.GetConfig().SetString(ConfigConstants.CONFIG_BRANCH_SECTION, branchName, ConfigConstants
				.CONFIG_KEY_MERGE, head.GetName());
			repo.GetConfig().Save();
		}

		/// <exception cref="NGit.Errors.MissingObjectException"></exception>
		/// <exception cref="NGit.Errors.IncorrectObjectTypeException"></exception>
		/// <exception cref="System.IO.IOException"></exception>
		private RevCommit ParseCommit(Repository repo, Ref @ref)
		{
			RevWalk rw = new RevWalk(repo);
			RevCommit commit;
			try
			{
				commit = rw.ParseCommit(@ref.GetObjectId());
			}
			finally
			{
				rw.Release();
			}
			return commit;
		}

		/// <param name="uri">the uri to clone from</param>
		/// <returns>this instance</returns>
		public virtual CloneCommand SetURI(string uri)
		{
			this.uri = uri;
			return this;
		}

		/// <summary>The optional directory associated with the clone operation.</summary>
		/// <remarks>
		/// The optional directory associated with the clone operation. If the
		/// directory isn't set, a name associated with the source uri will be used.
		/// </remarks>
		/// <seealso cref="NGit.Transport.URIish.GetHumanishName()">NGit.Transport.URIish.GetHumanishName()
		/// 	</seealso>
		/// <param name="directory">the directory to clone to</param>
		/// <returns>this instance</returns>
		public virtual CloneCommand SetDirectory(FilePath directory)
		{
			this.directory = directory;
			return this;
		}

		/// <param name="bare">whether the cloned repository is bare or not</param>
		/// <returns>this instance</returns>
		public virtual CloneCommand SetBare(bool bare)
		{
			this.bare = bare;
			return this;
		}

		/// <param name="remote">the branch to keep track of in the origin repository</param>
		/// <returns>this instance</returns>
		public virtual CloneCommand SetRemote(string remote)
		{
			this.remote = remote;
			return this;
		}

		/// <param name="branch">the initial branch to check out when cloning the repository</param>
		/// <returns>this instance</returns>
		public virtual CloneCommand SetBranch(string branch)
		{
			this.branch = branch;
			return this;
		}

		/// <summary>The progress monitor associated with the clone operation.</summary>
		/// <remarks>
		/// The progress monitor associated with the clone operation. By default,
		/// this is set to <code>NullProgressMonitor</code>
		/// </remarks>
		/// <seealso cref="NGit.NullProgressMonitor">NGit.NullProgressMonitor</seealso>
		/// <param name="monitor"></param>
		/// <returns>
		/// 
		/// <code>this</code>
		/// </returns>
		public virtual CloneCommand SetProgressMonitor(ProgressMonitor monitor)
		{
			this.monitor = monitor;
			return this;
		}

		/// <param name="credentialsProvider">
		/// the
		/// <see cref="NGit.Transport.CredentialsProvider">NGit.Transport.CredentialsProvider
		/// 	</see>
		/// to use
		/// </param>
		/// <returns>
		/// 
		/// <code>this</code>
		/// </returns>
		public virtual CloneCommand SetCredentialsProvider(CredentialsProvider credentialsProvider
			)
		{
			this.credentialsProvider = credentialsProvider;
			return this;
		}

		/// <param name="timeout">the timeout used for the fetch step</param>
		/// <returns>
		/// 
		/// <code>this</code>
		/// </returns>
		public virtual CloneCommand SetTimeout(int timeout)
		{
			this.timeout = timeout;
			return this;
		}

		/// <param name="cloneAllBranches">
		/// true when all branches have to be fetched (indicates wildcard
		/// in created fetch refspec), false otherwise.
		/// </param>
		/// <returns>
		/// 
		/// <code>this</code>
		/// </returns>
		public virtual CloneCommand SetCloneAllBranches(bool cloneAllBranches)
		{
			this.cloneAllBranches = cloneAllBranches;
			return this;
		}

		/// <param name="branchesToClone">
		/// collection of branches to clone. Ignored when allSelected is
		/// true.
		/// </param>
		/// <returns>
		/// 
		/// <code>this</code>
		/// </returns>
		public virtual CloneCommand SetBranchesToClone(ICollection<string> branchesToClone
			)
		{
			this.branchesToClone = branchesToClone;
			return this;
		}
	}
}
