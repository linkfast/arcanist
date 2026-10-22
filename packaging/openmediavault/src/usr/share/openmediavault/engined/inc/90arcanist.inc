<?php
/**
 * Arcanist's log in Diagnostics > System Logs: the arcanist service's journal.
 *
 * Copyright (C) 2026 LinkFast S.A. and Arcanist contributors
 * SPDX-License-Identifier: AGPL-3.0-or-later
 */
require_once("openmediavault/functions.inc");

// 2026-10-22T10:11:12+0200 omvhost arcanist[1234]: message (OMV 8 writes the offset as +02:00)
\OMV\System\LogFileSpec::registerSpecification("arcanist", [
	"command" => "export SYSTEMD_COLORS=0; journalctl --unit=arcanist.service " .
		"--quiet --output=short-iso --no-pager",
	"filename" => "arcanist",
	"regex" => "/^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:?\d{2})\s+(\S+)\s+\S+\[\d+\]:\s+(.*)$/",
	"columns" => [
		"ts" => [
			"index" => 1,
			"func" => function ($v) { return strpdate($v, "Y-m-d*H:i:sT"); }
		],
		"date" => [
			"index" => 1,
			"func" => function ($v) { return date("Y-m-d H:i:s", strpdate($v, "Y-m-d*H:i:sT")); }
		],
		"hostname" => 2,
		"message" => 3
	]
]);
