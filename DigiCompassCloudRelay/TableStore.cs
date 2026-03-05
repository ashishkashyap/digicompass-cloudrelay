using Azure.Data.Tables;

namespace DigiCompassCloudRelay;

public static class TableStore
{
    private static readonly TableServiceClient _svc =
        new TableServiceClient(RelayConfig.TablesConnection);

    public static TableClient Get(string name)
    {
        var table = _svc.GetTableClient(name);
        table.CreateIfNotExists();
        return table;
    }
}