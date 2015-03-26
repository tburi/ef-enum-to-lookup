﻿namespace EfEnumToLookup.LookupGenerator
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Data.Entity;
	using System.Data.Entity.Infrastructure;
	using System.Data.SqlClient;
	using System.Linq;
	using System.Reflection;
	using System.Text;
	using System.Text.RegularExpressions;

	/// <summary>
	/// Makes up for a missing feature in Entity Framework 6.1
	/// Creates lookup tables and foreign key constraints based on the enums
	/// used in your model.
	/// Use the properties exposed to control behaviour.
	/// Run <c>Apply</c> from your Seed method in either your database initializer
	/// or your EF Migrations.
	/// It is safe to run repeatedly, and will ensure enum values are kept in line
	/// with your current code.
	/// Source code: https://github.com/timabell/ef-enum-to-lookup
	/// License: MIT
	/// </summary>
	public class EnumToLookup : IEnumToLookup
	{
		private readonly EnumParser _enumParser;

		public EnumToLookup()
		{
			// set default behaviour, can be overridden by setting properties on object before calling Apply()
			NameFieldLength = 255;
			TableNamePrefix = "Enum_";
			_enumParser = new EnumParser { SplitWords = true };
		}

		/// <summary>
		/// If set to true (default) enum names will have spaces inserted between
		/// PascalCase words, e.g. enum SomeValue is stored as "Some Value".
		/// </summary>
		public bool SplitWords
		{
			set { _enumParser.SplitWords = value; }
			get { return _enumParser.SplitWords; }
		}

		/// <summary>
		/// The size of the Name field that will be added to the generated lookup tables.
		/// Adjust to suit your data if required, defaults to 255.
		/// </summary>
		public int NameFieldLength { get; set; }

		/// <summary>
		/// Prefix to add to all the generated tables to separate help group them together
		/// and make them stand out as different from other tables.
		/// Defaults to "Enum_" set to null or "" to not have any prefix.
		/// </summary>
		public string TableNamePrefix { get; set; }

		/// <summary>
		/// Suffix to add to all the generated tables to separate help group them together
		/// and make them stand out as different from other tables.
		/// Defaults to "" set to null or "" to not have any suffix.
		/// </summary>
		public string TableNameSuffix { get; set; }

		/// <summary>
		/// Create any missing lookup tables,
		/// enforce values in the lookup tables
		/// by way of a T-SQL MERGE
		/// </summary>
		/// <param name="context">EF Database context to search for enum references,
		///  context.Database.ExecuteSqlCommand() is used to apply changes.</param>
		public void Apply(DbContext context)
		{
			// recurse through dbsets and references finding anything that uses an enum
			var enumReferences = FindEnumReferences(context);

			// for the list of enums generate and missing tables
			var enums = enumReferences.Select(r => r.EnumType).Distinct().ToList();

			var lookups = (
				from enm in enums
				select new LookupData
				{
					Name = enm.Name,
					NumericType = enm.GetEnumUnderlyingType(),
					Values = _enumParser.GetLookupValues(enm),
				}).ToList();

			// todo: support MariaDb etc. Issue #16
			IDbHandler dbHandler = new SqlServerHandler();
			dbHandler.NameFieldLength = NameFieldLength;
			dbHandler.TableNamePrefix = TableNamePrefix;
			dbHandler.TableNameSuffix = TableNameSuffix;

			dbHandler.Apply(lookups, enumReferences, (sql, parameters) => ExecuteSqlCommand(context, sql, parameters));
		}

		private static int ExecuteSqlCommand(DbContext context, string sql, IEnumerable<SqlParameter> parameters = null)
		{
			if (parameters == null)
			{
				return context.Database.ExecuteSqlCommand(sql);
			}
			return context.Database.ExecuteSqlCommand(sql, parameters.Cast<object>().ToArray());
		}


		internal IList<EnumReference> FindEnumReferences(DbContext context)
		{
			var metadataWorkspace = ((IObjectContextAdapter)context).ObjectContext.MetadataWorkspace;

			var metadataHandler = new MetadataHandler();
			return metadataHandler.FindEnumReferences(metadataWorkspace);
		}

		internal IList<PropertyInfo> FindDbSets(Type contextType)
		{
			return contextType.GetProperties()
				.Where(p => p.PropertyType.IsGenericType
										&& p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
				.ToList();
		}

		internal IList<PropertyInfo> FindEnums(Type type)
		{
			return type.GetProperties()
				.Where(p => p.PropertyType.IsEnum
										|| (p.PropertyType.IsGenericType && p.PropertyType.GenericTypeArguments.First().IsEnum))
				.ToList();
		}
	}
}
