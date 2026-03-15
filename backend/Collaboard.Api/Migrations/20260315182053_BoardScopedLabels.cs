using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collaboard.Api.Migrations;

/// <inheritdoc />
public partial class BoardScopedLabels : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Labels_Name",
            table: "Labels");

        migrationBuilder.AddColumn<Guid>(
            name: "BoardId",
            table: "Labels",
            type: "TEXT",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        // Data migration: clone global labels into each board and remap CardLabels.
        // For each board, create board-scoped copies of every global label (BoardId = empty guid).
        // Then update CardLabels to point to the board-scoped copy based on the card's board.
        // Finally, delete the original global labels.
        migrationBuilder.Sql("""
            -- Create a temp table to hold the mapping from old label to new board-scoped label
            CREATE TEMP TABLE _label_mapping (
                OldLabelId TEXT NOT NULL,
                NewLabelId TEXT NOT NULL,
                BoardId TEXT NOT NULL
            );

            -- For each board, clone each global label with a new ID
            INSERT INTO _label_mapping (OldLabelId, NewLabelId, BoardId)
            SELECT
                l.Id,
                lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                b.Id
            FROM Labels l
            CROSS JOIN Boards b
            WHERE l.BoardId = '00000000-0000-0000-0000-000000000000';

            -- Insert the board-scoped label copies
            INSERT INTO Labels (Id, BoardId, Name, Color)
            SELECT m.NewLabelId, m.BoardId, l.Name, l.Color
            FROM _label_mapping m
            JOIN Labels l ON l.Id = m.OldLabelId;

            -- Remap CardLabels: for each card-label assignment, find the card's board
            -- (card -> lane -> board) and update to the matching board-scoped label
            CREATE TEMP TABLE _cardlabel_remap (
                CardId TEXT NOT NULL,
                OldLabelId TEXT NOT NULL,
                NewLabelId TEXT NOT NULL
            );

            INSERT INTO _cardlabel_remap (CardId, OldLabelId, NewLabelId)
            SELECT cl.CardId, cl.LabelId, m.NewLabelId
            FROM CardLabels cl
            JOIN Cards c ON c.Id = cl.CardId
            JOIN Lanes la ON la.Id = c.LaneId
            JOIN _label_mapping m ON m.OldLabelId = cl.LabelId AND m.BoardId = la.BoardId;

            -- Delete old card-label assignments that reference global labels
            DELETE FROM CardLabels
            WHERE LabelId IN (SELECT OldLabelId FROM _label_mapping);

            -- Insert the remapped card-label assignments
            INSERT INTO CardLabels (CardId, LabelId)
            SELECT CardId, NewLabelId FROM _cardlabel_remap;

            -- Delete the original global labels
            DELETE FROM Labels
            WHERE BoardId = '00000000-0000-0000-0000-000000000000';

            DROP TABLE _cardlabel_remap;
            DROP TABLE _label_mapping;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Labels_BoardId_Name",
            table: "Labels",
            columns: ["BoardId", "Name"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Labels_BoardId_Name",
            table: "Labels");

        migrationBuilder.DropColumn(
            name: "BoardId",
            table: "Labels");

        migrationBuilder.CreateIndex(
            name: "IX_Labels_Name",
            table: "Labels",
            column: "Name",
            unique: true);
    }
}
