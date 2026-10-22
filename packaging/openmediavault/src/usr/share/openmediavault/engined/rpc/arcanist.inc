<?php
/**
 * Arcanist's plugin for openmediavault: the service's settings, the shared folders it may use, and its status.
 * Arcanist itself (libraries, users, everything else) is set up in its own web interface.
 *
 * Copyright (C) 2026 LinkFast S.A. and Arcanist contributors
 * SPDX-License-Identifier: AGPL-3.0-or-later
 */
class OMVRpcServiceArcanist extends \OMV\Rpc\ServiceAbstract
{
	/** The data directory of the arcanist package (systemd's StateDirectory). */
	const DATA_DIR = "/var/lib/arcanist";

	public function getName()
	{
		return "Arcanist";
	}

	public function initialize()
	{
		$this->registerMethod("getSettings");
		$this->registerMethod("setSettings");
		$this->registerMethod("getShareList");
		$this->registerMethod("getShare");
		$this->registerMethod("setShare");
		$this->registerMethod("deleteShare");
		$this->registerMethod("getInfo");
		$this->registerMethod("restart");
	}

	public function getSettings($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$db = \OMV\Config\Database::getInstance();
		$object = $db->get("conf.service.arcanist");
		$object->remove("shares");
		return $object->getAssoc();
	}

	public function setSettings($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$this->validateMethodParams($params, "rpc.arcanist.setsettings");
		$db = \OMV\Config\Database::getInstance();
		$object = $db->get("conf.service.arcanist");
		// The shared folders stay as they are.
		$object->setAssoc($params);
		$db->set($object);
		$object->remove("shares");
		return $object->getAssoc();
	}

	public function getShareList($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$this->validateMethodParams($params, "rpc.common.getlist");
		$db = \OMV\Config\Database::getInstance();
		$result = [];
		foreach ($db->get("conf.service.arcanist.share") as $objectv) {
			$sf = $db->get("conf.system.sharedfolder", $objectv->get("sharedfolderref"));
			$objectv->add("sharedfoldername", "string", $sf->get("name"));
			// The absolute path, the one to choose in Arcanist's library settings.
			$objectv->add("sharedfolderpath", "string", \OMV\Rpc\Rpc::call("ShareMgmt", "getPath",
				["uuid" => $objectv->get("sharedfolderref")], $context));
			$result[] = $objectv->getAssoc();
		}
		return $this->applyFilter($result, $params['start'], $params['limit'],
			$params['sortfield'], $params['sortdir']);
	}

	public function getShare($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$this->validateMethodParams($params, "rpc.common.objectuuid");
		$db = \OMV\Config\Database::getInstance();
		return $db->getAssoc("conf.service.arcanist.share", $params['uuid']);
	}

	public function setShare($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$this->validateMethodParams($params, "rpc.arcanist.setshare");
		$object = new \OMV\Config\ConfigObject("conf.service.arcanist.share");
		$object->setAssoc($params);
		$db = \OMV\Config\Database::getInstance();
		if (true === $object->isNew()) {
			$db->assertIsUnique($object, "sharedfolderref");
		}
		$db->set($object);
		return $object->getAssoc();
	}

	public function deleteShare($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$this->validateMethodParams($params, "rpc.common.objectuuid");
		$db = \OMV\Config\Database::getInstance();
		$object = $db->get("conf.service.arcanist.share", $params['uuid']);
		$db->delete($object);
		return $object->getAssoc();
	}

	/** The service's state, its version and addresses, and the setup code while setup is pending, as text. */
	public function getInfo($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$db = \OMV\Config\Database::getInstance();
		$object = $db->get("conf.service.arcanist");
		$systemCtl = new \OMV\System\SystemCtl("arcanist");
		$lines = [];
		$lines[] = sprintf(gettext("Service: %s"), $systemCtl->isActive() ?
			gettext("running") : gettext("not running"));
		$lines[] = sprintf(gettext("Port: %d"), $object->get("port"));
		// runtime.json exists while the server runs.
		$runtime = self::DATA_DIR . "/runtime.json";
		if (is_readable($runtime)) {
			$info = json_decode(file_get_contents($runtime), true);
			if (is_array($info)) {
				$lines[] = sprintf(gettext("Version: %s"), $info['version'] ?? "?");
				$lines[] = sprintf(gettext("Started: %s"), $info['startedAt'] ?? "?");
			}
		}
		$lines[] = "";
		$codeFile = self::DATA_DIR . "/setup-code.txt";
		$code = is_readable($codeFile) ? trim(file_get_contents($codeFile)) : "";
		if ($code !== "") {
			$lines[] = sprintf(gettext("Setup code: %s"), $code);
			$lines[] = gettext("Open Arcanist and enter this code in its setup wizard.");
		} else {
			$lines[] = gettext("Arcanist is set up: no setup code is needed.");
		}
		$lines[] = "";
		$lines[] = gettext("Its log is in Diagnostics > System Logs > Arcanist.");
		return implode("\n", $lines);
	}

	public function restart($params, $context)
	{
		$this->validateMethodContext($context, ["role" => OMV_ROLE_ADMINISTRATOR]);
		$db = \OMV\Config\Database::getInstance();
		if (true !== $db->get("conf.service.arcanist")->get("enable")) {
			return;
		}
		(new \OMV\System\SystemCtl("arcanist"))->restart();
	}
}
