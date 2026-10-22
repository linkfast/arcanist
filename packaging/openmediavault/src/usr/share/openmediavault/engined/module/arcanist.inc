<?php
/**
 * Arcanist's module: marks the configuration as changed (the Apply banner, which deploys srv/salt/omv/deploy/arcanist)
 * and reports the service's state on the dashboard.
 *
 * Copyright (C) 2026 LinkFast S.A. and Arcanist contributors
 * SPDX-License-Identifier: AGPL-3.0-or-later
 */
class OMVModuleArcanist extends \OMV\Engine\Module\ServiceAbstract
	implements \OMV\Engine\Notify\IListener, \OMV\Engine\Module\IServiceStatus
{
	public function getName()
	{
		return "arcanist";
	}

	public function getStatus()
	{
		$db = \OMV\Config\Database::getInstance();
		$object = $db->get("conf.service.arcanist");
		$systemCtl = new \OMV\System\SystemCtl("arcanist");
		return [
			"name" => $this->getName(),
			"title" => gettext("Arcanist"),
			"enabled" => $object->get("enable"),
			"running" => $systemCtl->isActive()
		];
	}

	final public function onSharedFolder($type, $path, $object)
	{
		$db = \OMV\Config\Database::getInstance();
		if (true === $db->exists("conf.service.arcanist.share", [
			"operator" => "stringEquals",
			"arg0" => "sharedfolderref",
			"arg1" => $object['uuid']
		])) {
			$this->setDirty();
		}
	}

	public function bindListeners(\OMV\Engine\Notify\Dispatcher $dispatcher)
	{
		$dispatcher->addListener(OMV_NOTIFY_MODIFY,
			"org.openmediavault.conf.service.arcanist", [$this, "setDirty"]);
		$dispatcher->addListener(OMV_NOTIFY_CREATE | OMV_NOTIFY_MODIFY | OMV_NOTIFY_DELETE,
			"org.openmediavault.conf.service.arcanist.share", [$this, "setDirty"]);
		$dispatcher->addListener(OMV_NOTIFY_MODIFY,
			"org.openmediavault.conf.system.sharedfolder", [$this, "onSharedFolder"]);
	}
}
