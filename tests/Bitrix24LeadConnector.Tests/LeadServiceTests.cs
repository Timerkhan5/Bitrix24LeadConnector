using System.Text.Json;
using Bitrix24LeadConnector.Api.Integrations.Bitrix24;
using Bitrix24LeadConnector.Api.Models;
using Bitrix24LeadConnector.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitrix24LeadConnector.Tests;

public sealed class LeadServiceTests
{
    private readonly RecordingHandler handler = new();

    private LeadService Service()
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/rest/1/test-token/") };
        return new LeadService(new Bitrix24Client(http, NullLogger<Bitrix24Client>.Instance), NullLogger<LeadService>.Instance);
    }

    [Fact]
    public async Task Valid_request_maps_fields_normalizes_phone_and_returns_CRM_id()
    {
        var response = await Service().CreateAsync(new("  Тестовый клиент  ", " +7 (000) 000-00-00 ", "  Тестовая заявка  "), default);
        Assert.True(response.Success);
        Assert.Equal(123, response.LeadId);
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(1, json.RootElement.GetProperty("entityTypeId").GetInt32());
        var fields = json.RootElement.GetProperty("fields");
        Assert.Equal("Заявка с сайта — Тестовый клиент", fields.GetProperty("title").GetString());
        Assert.Equal("Тестовый клиент", fields.GetProperty("name").GetString());
        Assert.Equal("Тестовая заявка", fields.GetProperty("comments").GetString());
        var phone = Assert.Single(fields.GetProperty("fm").EnumerateArray());
        Assert.Equal("PHONE", phone.GetProperty("typeId").GetString());
        Assert.Equal("WORK", phone.GetProperty("valueType").GetString());
        Assert.Equal("+70000000000", phone.GetProperty("value").GetString());
        Assert.EndsWith("/crm.item.add.json", handler.LastUri!.AbsolutePath);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  ")] [InlineData("Я")]
    public async Task Invalid_name_never_reaches_CRM(string? name)
    {
        var error = await Assert.ThrowsAsync<LeadValidationException>(() => Service().CreateAsync(new(name, "70000000000"), default));
        Assert.Contains("name", error.Errors.Keys);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Name_over_80_characters_never_reaches_CRM()
    {
        await Assert.ThrowsAsync<LeadValidationException>(() => Service().CreateAsync(new(new string('Я', 81), "70000000000"), default));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("700000000")] [InlineData("1234567890123456")]
    [InlineData("+70000000000ext")] [InlineData("7+0000000000")] [InlineData("++70000000000")]
    [InlineData("７００００００００００")] [InlineData("70000000000\n70000000000")]
    public async Task Invalid_phone_never_reaches_CRM(string? phone)
    {
        var error = await Assert.ThrowsAsync<LeadValidationException>(() => Service().CreateAsync(new("Тест", phone), default));
        Assert.Contains("phone", error.Errors.Keys);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Oversized_comment_never_reaches_CRM()
    {
        var error = await Assert.ThrowsAsync<LeadValidationException>(() => Service().CreateAsync(new("Тест", "70000000000", new string('а', 1001)), default));
        Assert.Contains("comment", error.Errors.Keys);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Boundary_lengths_and_missing_comment_are_accepted()
    {
        await Service().CreateAsync(new(new string('Я', 80), "123456789012345", new string('а', 1000)), default);
        await Service().CreateAsync(new("Те", "1234567890"), default);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task List_maps_only_phone_and_forwards_pagination()
    {
        handler.Respond = _ => Task.FromResult(RecordingHandler.Json("""
            {"result":{"items":[{"id":"123","title":"Тест","name":"Тестовый клиент","comments":"Тестовая заявка",
            "createdTime":"2026-11-10T12:00:00+03:00","fm":[
            {"typeId":"EMAIL","value":"example@example.invalid","valueType":"WORK"},
            {"typeId":"PHONE","value":"+70000000000","valueType":"WORK"}]}]},"total":120,"next":100}
            """));
        var response = await Service().ListAsync(50, default);
        var item = Assert.Single(response.Items);
        Assert.Equal(123, item.Id);
        Assert.Equal("+70000000000", item.Phone);
        Assert.Equal("Тестовая заявка", item.Comments);
        Assert.Equal(TimeSpan.FromHours(3), item.CreatedTime!.Value.Offset);
        Assert.Equal(120, response.Total);
        Assert.Equal(100, response.NextStart);
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(50, json.RootElement.GetProperty("start").GetInt32());
        Assert.Equal("*", json.RootElement.GetProperty("select")[0].GetString());
        Assert.Equal(1, json.RootElement.GetProperty("entityTypeId").GetInt32());
    }

    [Fact]
    public async Task Empty_list_returns_empty_array_and_no_next_page()
    {
        handler.Respond = _ => Task.FromResult(RecordingHandler.Json("""{"result":{"items":[]},"total":0}"""));
        var response = await Service().ListAsync(0, default);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.Total);
        Assert.Null(response.NextStart);
    }
}
