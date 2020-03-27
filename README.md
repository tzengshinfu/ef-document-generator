About
=====

Reads MSSQL table/column summary and then adds/updates the documentation in the matching `.emdx` and `.tt` files.

This is a fork of https://github.com/timabell/ef-document-generator fixed-up for EF6

Usage
=====

	EFTSQLDocumentation.Generator.exe 	\
		-c "server=.;database=yourdatabase;Integrated Security=SSPI"  \
		-i path\to\your\Model.edmx

Arguments
---------

* -c, --connectionString... ConnectionString of the documented database
* -i, --input... original edmx file
* -o, --output [optional] ... output edmx file - Default : original edmx file
