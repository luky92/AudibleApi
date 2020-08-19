﻿using System;
using System.Collections.Generic;
using System.IO;
using AudibleApi.Authorization;
using InternalUtilities;

namespace L1.Tests
{
    public static class REAL
    {
		private static string _tokenFilePath
		{
			get
			{
				try
				{
					return AudibleApiStorage.AccountsSettingsFile;
				}
				catch (Exception ex)
				{
					throw new Exception($"Error! settings file not found.\r\nTo fix this error, copy the client's appsettings LibationFiles path into the L1's appsettings.json LibationFiles entry");
				}
			}
		}

		public static Identity GetIdentity()
			=> Identity.FromJson(File.ReadAllText(_tokenFilePath), AudibleApiStorage.GetIdentityTokensJsonPath());

		public static IdentityPersister GetIdentityPersister()
			=> new IdentityPersister(_tokenFilePath, AudibleApiStorage.GetIdentityTokensJsonPath());
	}
}
